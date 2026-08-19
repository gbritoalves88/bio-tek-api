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

        // Método para obter token OAuth2
        private async Task<string> GetAccessTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiration)
            {
                return _accessToken;
            }

            try
            {
                // Ler o arquivo JSON da conta de serviço
                //var serviceAccountJson = await File.ReadAllTextAsync(_firebaseSettings.ServiceAccountPath);
                string serviceAccountJson;

                if (!string.IsNullOrEmpty(_firebaseSettings.ServiceAccountJson))
                {
                    // Usar JSON da variável de ambiente (produção)
                    serviceAccountJson = _firebaseSettings.ServiceAccountJson;
                }
                else if (!string.IsNullOrEmpty(_firebaseSettings.ServiceAccountPath))
                {
                    // Usar arquivo local (desenvolvimento)
                    serviceAccountJson = await File.ReadAllTextAsync(_firebaseSettings.ServiceAccountPath);
                }
                else
                {
                    throw new InvalidOperationException("Configuração Firebase não encontrada");
                }
                
                
                var serviceAccount = JsonSerializer.Deserialize<JsonElement>(serviceAccountJson);

                var clientEmail = serviceAccount.GetProperty("client_email").GetString();
                var privateKey = serviceAccount.GetProperty("private_key").GetString();

                // Criar JWT para autenticação
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

                // Trocar JWT por access token
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

        // Método para enviar notificação para um único token
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
                    _logger.LogInformation($"Notificação enviada com sucesso para token: {token.Substring(0, Math.Min(10, token.Length))}...");
                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
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

        // Método para enviar notificação para múltiplos tokens
        public async Task<bool> SendMulticastAsync(
            List<string> tokens,
            string title,
            string body,
            string url = null,
            Dictionary<string, string> data = null)
        {
            var success = true;
            
            foreach (var token in tokens)
            {
                if (!await SendNotificationAsync(token, title, body, url, data))
                {
                    success = false;
                }
            }
            
            return success;
        }

        // Método para enviar lembrete de leitura
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

                // Buscar tokens ativos do usuário
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

        // Método para validar token
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