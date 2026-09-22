using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Models;
using BioTek.Services.Interfaces;

namespace BioTek.Services
{
    public class FirebaseNotificationService : IFirebaseNotificationService
    {
        private readonly HttpClient _httpClient;
        private readonly FirebaseSettings _firebaseSettings;
        private readonly ILogger<FirebaseNotificationService> _logger;
        private readonly IRepository<NotificationSubscription> _subscriptionRepository;
        private string _accessToken;
        private DateTime _tokenExpiration;

        private const int MAX_TOKENS_POR_LOTE = 500;

        public FirebaseNotificationService(
            HttpClient httpClient,
            ILogger<FirebaseNotificationService> logger,
            IRepository<NotificationSubscription> subscriptionRepository,
            IOptions<FirebaseSettings> firebaseSettings)
        {
            _httpClient = httpClient;
            _logger = logger;
            _subscriptionRepository = subscriptionRepository;
            _firebaseSettings = firebaseSettings.Value;
            _tokenExpiration = DateTime.MinValue;
        }

        // Método para obter token OAuth2
        private async Task<string> GetAccessTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiration)
            {
                return _accessToken;
            }

            try
            {
                string serviceAccountJson;

                if (!string.IsNullOrEmpty(_firebaseSettings.ServiceAccountJson))
                {
                    serviceAccountJson = _firebaseSettings.ServiceAccountJson;
                }
                else if (!string.IsNullOrEmpty(_firebaseSettings.ServiceAccountPath))
                {
                    serviceAccountJson = await File.ReadAllTextAsync(_firebaseSettings.ServiceAccountPath);
                }
                else
                {
                    throw new InvalidOperationException("Configuração Firebase não encontrada");
                }

                var serviceAccount = JsonSerializer.Deserialize<JsonElement>(serviceAccountJson);

                var clientEmail = serviceAccount.GetProperty("client_email").GetString();
                var privateKey = serviceAccount.GetProperty("private_key").GetString();

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var jwtHeader = new { alg = "RS256", typ = "JWT" };
                var jwtClaimSet = new
                {
                    iss = clientEmail,
                    scope = "https://www.googleapis.com/auth/firebase.messaging",
                    aud = "https://oauth2.googleapis.com/token",
                    iat = now,
                    exp = now + 3600
                };

                var headerJson = JsonSerializer.Serialize(jwtHeader);
                var claimSetJson = JsonSerializer.Serialize(jwtClaimSet);

                var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
                var claimSetBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(claimSetJson));

                var signatureInput = $"{headerBase64}.{claimSetBase64}";
                var signature = SignWithRsa(privateKey, signatureInput);

                var jwt = $"{signatureInput}.{signature}";

                var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
                var formData = new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = jwt
                };
                tokenRequest.Content = new FormUrlEncodedContent(formData);

                var response = await _httpClient.SendAsync(tokenRequest);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);

                _accessToken = tokenResponse.GetProperty("access_token").GetString();
                _tokenExpiration = DateTime.UtcNow.AddMinutes(50);

                return _accessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter token de acesso Firebase");
                throw;
            }
        }

        // Enviar notificação para um único token
        public async Task<bool> SendNotificationAsync(
            string token,
            string title,
            string body,
            string url = null,
            Dictionary<string, string> data = null)
        {
            try
            {
                var accessToken = await GetAccessTokenAsync();
                var projectId = _firebaseSettings.ProjectId;

                var message = new
                {
                    message = new
                    {
                        token = token,
                        notification = new
                        {
                            title = title,
                            body = body
                        },
                        data = data ?? new Dictionary<string, string>(),
                        webpush = new
                        {
                            fcm_options = new
                            {
                                link = url ?? "/"
                            },
                            notification = new
                            {
                                icon = "/assets/icons/icon-192x192.png",
                                badge = "/assets/icons/icon-96x96.png",
                                click_action = url ?? "/"
                            }
                        },
                        android = new
                        {
                            priority = "high",
                            notification = new
                            {
                                icon = "ic_stat_notification",
                                color = "#DB7C00",
                                sound = "default",
                                //click_action = "FCM_PLUGIN_ACTIVITY",
                                channel_id = "default"
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(message);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

                var response = await _httpClient.PostAsync(
                    $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send",
                    content
                );

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();

                    if (error.Contains("UNREGISTERED") || error.Contains("INVALID_ARGUMENT"))
                    {
                        await DesativarTokenAsync(token);
                    }

                    _logger.LogError($"Erro ao enviar notificação: {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao enviar notificação Firebase");
                return false;
            }
        }

        // Enviar para múltiplos tokens (paralelo)
        public async Task<bool> SendMulticastAsync(
            List<string> tokens,
            string title,
            string body,
            string url = null,
            Dictionary<string, string> data = null)
        {
            if (tokens == null || tokens.Count == 0)
                return false;

            var tarefas = tokens.Select(token =>
                SendNotificationAsync(token, title, body, url, data)
            ).ToArray();

            var resultados = await Task.WhenAll(tarefas);

            return resultados.All(r => r);
        }

        // Enviar lote usando multicast real do Firebase
        private async Task<bool> EnviarLoteMulticastAsync(
            List<string> tokens,
            string title,
            string body,
            string url,
            Dictionary<string, string> data)
        {
            var accessToken = await GetAccessTokenAsync();
            var projectId = _firebaseSettings.ProjectId;

            var message = new
            {
                message = new
                {
                    tokens = tokens.ToArray(),
                    notification = new
                    {
                        title = title,
                        body = body
                    },
                    data = data ?? new Dictionary<string, string>(),
                    webpush = new
                    {
                        fcm_options = new
                        {
                            link = url ?? "/"
                        }
                    },
                    android = new
                    {
                        priority = "high",
                        notification = new
                        {
                            icon = "ic_stat_notification",
                            color = "#DB7C00",
                            sound = "default",
                            //click_action = "FCM_PLUGIN_ACTIVITY",
                            channel_id = "default"
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(message);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.PostAsync(
                $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send",
                content
            );

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                await ProcessarRespostaMulticast(result);
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Erro no multicast: {error}");
                return false;
            }
        }

        // Processar resposta e desativar tokens inválidos
        private async Task ProcessarRespostaMulticast(string respostaJson)
        {
            try
            {
                var resposta = JsonSerializer.Deserialize<JsonElement>(respostaJson);

                if (resposta.TryGetProperty("responses", out var responses))
                {
                    foreach (var resp in responses.EnumerateArray())
                    {
                        if (resp.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                        {
                            if (resp.TryGetProperty("error", out var error))
                            {
                                var errorMessage = error.ToString();

                                if (errorMessage.Contains("UNREGISTERED") ||
                                    errorMessage.Contains("INVALID_ARGUMENT") ||
                                    errorMessage.Contains("NOT_FOUND"))
                                {
                                    _logger.LogWarning($"Token inválido detectado: {errorMessage}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar resposta do multicast");
            }
        }

        // Desativar token inválido
        private async Task DesativarTokenAsync(string token)
        {
            try
            {
                var subscriptions = await _subscriptionRepository.FindAsync(s => s.Token == token);

                foreach (var sub in subscriptions)
                {
                    sub.IsActive = false;
                    sub.LastUsedAt = DateTime.UtcNow;
                    await _subscriptionRepository.UpdateAsync(sub.Id, sub);
                    _logger.LogInformation($"Token desativado: {token.Substring(0, Math.Min(10, token.Length))}...");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao desativar token inválido");
            }
        }

        // Enviar lembrete de leitura
        public async Task<bool> SendReadingReminderAsync(string userId, string bookTitle, string bookId)
        {
            try
            {
                var title = "📖 Continue sua leitura!";
                var body = $"Você começou a ler '{bookTitle}' e está há um tempo sem atividade. Que tal continuar de onde parou?";
                var url = $"/livros/{bookId}";

                var data = new Dictionary<string, string>
                {
                    { "bookId", bookId },
                    { "bookTitle", bookTitle },
                    { "type", "reading_reminder" },
                    { "userId", userId }
                };

                var subscriptions = await _subscriptionRepository.FindAsync(s =>
                    s.UserId == userId && s.IsActive);

                var tokens = subscriptions.Select(s => s.Token).ToList();

                if (tokens.Count == 0)
                {
                    _logger.LogInformation($"Nenhum token ativo encontrado para o usuário {userId}");
                    return false;
                }

                return await SendMulticastAsync(tokens, title, body, url, data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao enviar lembrete de leitura");
                return false;
            }
        }

        // Validar token
        public async Task<bool> ValidateTokenAsync(string token)
        {
            try
            {
                var accessToken = await GetAccessTokenAsync();
                var projectId = _firebaseSettings.ProjectId;

                var validationMessage = new
                {
                    validate_only = true,
                    message = new
                    {
                        token = token
                    }
                };

                var json = JsonSerializer.Serialize(validationMessage);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

                var response = await _httpClient.PostAsync(
                    $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send",
                    content
                );

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar token Firebase");
                return false;
            }
        }

        // Métodos auxiliares
        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        private static string SignWithRsa(string privateKey, string data)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKey.ToCharArray());

            var dataBytes = Encoding.UTF8.GetBytes(data);
            var signatureBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return Base64UrlEncode(signatureBytes);
        }
    }
}