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
        private async Task<string> GetAccessTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiration)
            {
                _logger.LogDebug("[FIREBASE] Reutilizando access token em cache.");
                return _accessToken;
            }

            try
            {
                _logger.LogInformation("[FIREBASE] Obtendo novo access token do Google...");

                string serviceAccountJson;

                if (!string.IsNullOrEmpty(_firebaseSettings.ServiceAccountJson))
                {
                    serviceAccountJson = _firebaseSettings.ServiceAccountJson;
                    _logger.LogInformation("[FIREBASE] Usando ServiceAccountJson das configurações.");
                }
                else if (!string.IsNullOrEmpty(_firebaseSettings.ServiceAccountPath))
                {
                    serviceAccountJson = await File.ReadAllTextAsync(_firebaseSettings.ServiceAccountPath);
                    _logger.LogInformation($"[FIREBASE] Usando ServiceAccountPath: {_firebaseSettings.ServiceAccountPath}");
                }
                else
                {
                    _logger.LogError("[FIREBASE] ❌ Nenhuma credencial encontrada (ServiceAccountJson nem ServiceAccountPath).");
                    throw new InvalidOperationException("Configuração Firebase não encontrada");
                }

                var serviceAccount = JsonSerializer.Deserialize<JsonElement>(serviceAccountJson);

                var clientEmail = serviceAccount.GetProperty("client_email").GetString();
                var privateKey = serviceAccount.GetProperty("private_key").GetString();

                _logger.LogInformation($"[FIREBASE] client_email: {clientEmail}");

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

                if (!response.IsSuccessStatusCode)
                {
                    var erro = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"[FIREBASE] ❌ Google recusou o token: {response.StatusCode} — {erro}");
                    response.EnsureSuccessStatusCode();
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);

                _accessToken = tokenResponse.GetProperty("access_token").GetString();
                _tokenExpiration = DateTime.UtcNow.AddMinutes(50);

                _logger.LogInformation($"[FIREBASE] ✅ Access token obtido com sucesso (válido até {_tokenExpiration:HH:mm:ss} UTC).");

                return _accessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FIREBASE] ❌ Falha ao obter access token.");
                throw;
            }
        }

        public async Task<bool> SendNotificationAsync(
            string token,
            string title,
            string body,
            string url = null,
            Dictionary<string, string> data = null)
        {
            var tokenCurto = token.Substring(0, Math.Min(10, token.Length));

            try
            {
                _logger.LogInformation($"[FIREBASE] Enviando push para token {tokenCurto}...");

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
                                click_action = "FCM_PLUGIN_ACTIVITY",
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
                    _logger.LogInformation($"[FIREBASE] ✅ Push enviado para token {tokenCurto}.");
                    return true;
                }

                var error = await response.Content.ReadAsStringAsync();

                if (error.Contains("UNREGISTERED") || error.Contains("INVALID_ARGUMENT") || error.Contains("NOT_FOUND"))
                {
                    _logger.LogWarning($"[FIREBASE] ⚠️ Token {tokenCurto} inválido. Desativando...");
                    await DesativarTokenAsync(token);
                }

                _logger.LogError($"[FIREBASE] ❌ Erro ao enviar para token {tokenCurto}: {response.StatusCode} — {error}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[FIREBASE] ❌ Exceção ao enviar para token {tokenCurto}.");
                return false;
            }
        }
        
        public async Task<bool> SendMulticastAsync(
            List<string> tokens,
            string title,
            string body,
            string url = null,
            Dictionary<string, string> data = null)
        {
            if (tokens == null || tokens.Count == 0)
            {
                _logger.LogWarning("[FIREBASE] SendMulticastAsync recebeu lista de tokens vazia.");
                return false;
            }

            _logger.LogInformation($"[FIREBASE] Multicast iniciando: {tokens.Count} token(s).");

            var tarefas = tokens.Select(token =>
                SendNotificationAsync(token, title, body, url, data)
            ).ToArray();

            var resultados = await Task.WhenAll(tarefas);

            var enviados = resultados.Count(r => r);

            _logger.LogInformation($"[FIREBASE] Multicast finalizado: {enviados}/{tokens.Count} tokens receberam.");

            return enviados > 0;
        }
        public async Task<bool> SendReadingReminderAsync(string userId, string bookTitle, string bookId)
        {
            try
            {
                var title = "📖 Continue sua leitura!";
                var body = $"Você começou a ler '{bookTitle}' e está há um tempo sem atividade. Que tal continuar de onde parou?";
                var url = $"/inicio/livro/{bookId}";

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
                    _logger.LogInformation($"[FIREBASE] Nenhum token ativo para {userId}");
                    return false;
                }

                return await SendMulticastAsync(tokens, title, body, url, data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FIREBASE] Erro no lembrete de leitura");
                return false;
            }
        }

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
                _logger.LogError(ex, "[FIREBASE] Erro ao validar token");
                return false;
            }
        }

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
                    _logger.LogInformation($"[FIREBASE] Token desativado: {token.Substring(0, Math.Min(10, token.Length))}...");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FIREBASE] Erro ao desativar token");
            }
        }

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