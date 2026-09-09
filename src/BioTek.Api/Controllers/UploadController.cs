using System.Security.Claims;
using BioTek.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("UploadPolicy")]
public class UploadController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UploadController> _logger;

    // 🆕 Constantes de validação
    private const int MAX_IMAGE_SIZE_MB = 5;
    private const int MAX_IMAGE_SIZE_BYTES = MAX_IMAGE_SIZE_MB * 1024 * 1024;
    private static readonly string[] TiposPermitidos = new[]
    {
        "data:image/jpeg;base64,",
        "data:image/png;base64,",
        "data:image/webp;base64,",
        "data:image/gif;base64,"
    };

    public UploadController(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<UploadController> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Upload de imagem via ImgBB (API Key protegida no backend)
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("UploadPolicy")]
    [RequestSizeLimit(MAX_IMAGE_SIZE_BYTES + 1024)] // 🆕 +1KB para overhead do JSON
    public async Task<IActionResult> Upload([FromBody] UploadImagemRequest request)
    {
        try
        {
            // 🆕 Validar request
            if (request == null || string.IsNullOrEmpty(request.Base64))
                return BadRequest(new { mensagem = "Imagem é obrigatória" });

            // 🆕 Validar formato do base64
            if (!IsValidImageFormat(request.Base64))
                return BadRequest(new { mensagem = "Formato de imagem inválido. Use JPEG, PNG, WebP ou GIF" });

            // 🆕 Extrair e validar base64
            var imagemLimpa = ExtrairBase64(request.Base64);
            if (string.IsNullOrEmpty(imagemLimpa))
                return BadRequest(new { mensagem = "Base64 inválido" });

            // 🆕 Validar tamanho
            var tamanhoBytes = (imagemLimpa.Length * 3) / 4; // Aproximação do tamanho real
            if (tamanhoBytes > MAX_IMAGE_SIZE_BYTES)
                return BadRequest(new { mensagem = $"Imagem muito grande. Máximo: {MAX_IMAGE_SIZE_MB}MB" });

            // 🆕 Validar se é base64 válido
            if (!IsValidBase64(imagemLimpa))
                return BadRequest(new { mensagem = "Base64 inválido" });

            // 🆕 Buscar configurações
            var apiKey = _configuration["ImgBB:imgbbApiKey"];
            var imgUrl = _configuration["ImgBB:imgbbUrl"];

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(imgUrl))
            {
                _logger.LogError("Configuração ImgBB não encontrada");
                return StatusCode(500, new { mensagem = "Serviço de upload indisponível" });
            }

            // 🆕 Usar HttpClientFactory (melhor performance)
            using var httpClient = _httpClientFactory.CreateClient("ImgBB");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["key"] = apiKey,
                ["image"] = imagemLimpa
            });

            var response = await httpClient.PostAsync(imgUrl, content);
            var resultJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Erro ImgBB: {response.StatusCode} - {resultJson}");
                return BadRequest(new { mensagem = "Erro ao fazer upload da imagem" });
            }

            // 🆕 Parse seguro do JSON
            var resultObj = JsonDocument.Parse(resultJson);
            if (!resultObj.RootElement.TryGetProperty("data", out var dataElement) ||
                !dataElement.TryGetProperty("url", out var urlElement))
            {
                _logger.LogError($"Resposta ImgBB sem URL: {resultJson}");
                return StatusCode(500, new { mensagem = "Erro ao processar resposta do upload" });
            }

            var url = urlElement.GetString();
            if (string.IsNullOrEmpty(url))
                return StatusCode(500, new { mensagem = "URL da imagem não retornada" });

            // 🆕 Log de sucesso
            var usuarioId = GetUsuarioId();
            _logger.LogInformation($"Upload de imagem realizado pelo usuário {usuarioId}: {url}");

            return Ok(new { url });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Erro ao parsear resposta do ImgBB");
            return StatusCode(500, new { mensagem = "Erro ao processar resposta do upload" });
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Timeout no upload de imagem");
            return StatusCode(408, new { mensagem = "Tempo esgotado no upload" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado no upload de imagem");
            return StatusCode(500, new { mensagem = "Erro interno no upload" });
        }
    }

    // ============ MÉTODOS AUXILIARES ============

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? "desconhecido";
    }

    private bool IsValidImageFormat(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return false;
        
        return TiposPermitidos.Any(tipo => base64.StartsWith(tipo));
    }

    private string? ExtrairBase64(string base64ComPrefixo)
    {
        try
        {
            var virgulaIndex = base64ComPrefixo.IndexOf(',');
            if (virgulaIndex < 0 || virgulaIndex >= base64ComPrefixo.Length - 1)
                return null;

            return base64ComPrefixo[(virgulaIndex + 1)..];
        }
        catch
        {
            return null;
        }
    }

    private bool IsValidBase64(string base64)
    {
        try
        {
            // 🆕 Validar caracteres do base64
            if (base64.Any(c => !char.IsLetterOrDigit(c) && c != '+' && c != '/' && c != '='))
                return false;

            // 🆕 Validar comprimento (múltiplo de 4)
            if (base64.Length % 4 != 0)
                return false;

            // 🆕 Tentar converter
            Convert.FromBase64String(base64);
            return true;
        }
        catch
        {
            return false;
        }
    }
}