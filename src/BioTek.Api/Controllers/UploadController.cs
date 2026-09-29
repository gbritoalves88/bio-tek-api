using System.Security.Claims;
using BioTek.Api.Models;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("UploadPolicy")]
public class UploadController : ControllerBase
{
    private readonly IR2Service _r2Service;
    private readonly ILogger<UploadController> _logger;

    private const int MAX_IMAGE_SIZE_MB = 5;
    private const int MAX_IMAGE_SIZE_BYTES = MAX_IMAGE_SIZE_MB * 1024 * 1024;

    private static readonly Dictionary<string, string> TiposPermitidos = new()
    {
        { "data:image/jpeg;base64,", ".jpg" },
        { "data:image/png;base64,",  ".png" },
        { "data:image/webp;base64,", ".webp" },
        { "data:image/gif;base64,",  ".gif" }
    };

    public UploadController(IR2Service r2Service, ILogger<UploadController> logger)
    {
        _r2Service = r2Service;
        _logger = logger;
    }

    /// <summary>
    /// Upload de imagem para o Cloudflare R2
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("UploadPolicy")]
    [RequestSizeLimit(MAX_IMAGE_SIZE_BYTES + 1024)]
    public async Task<IActionResult> Upload([FromBody] UploadImagemRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrEmpty(request.Base64))
                return BadRequest(new { mensagem = "Imagem é obrigatória" });

            // Detecta tipo + extensão
            var tipo = TiposPermitidos.FirstOrDefault(t => request.Base64.StartsWith(t.Key));
            if (string.IsNullOrEmpty(tipo.Key))
                return BadRequest(new { mensagem = "Formato de imagem inválido. Use JPEG, PNG, WebP ou GIF" });

            var extensao = tipo.Value;
            var contentType = tipo.Key.Replace("data:", "").Replace(";base64,", "");

            // Extrai base64
            var imagemLimpa = ExtrairBase64(request.Base64);
            if (string.IsNullOrEmpty(imagemLimpa))
                return BadRequest(new { mensagem = "Base64 inválido" });

            // Valida tamanho
            var tamanhoBytes = (imagemLimpa.Length * 3) / 4;
            if (tamanhoBytes > MAX_IMAGE_SIZE_BYTES)
                return BadRequest(new { mensagem = $"Imagem muito grande. Máximo: {MAX_IMAGE_SIZE_MB}MB" });

            // Valida base64
            if (!IsValidBase64(imagemLimpa))
                return BadRequest(new { mensagem = "Base64 inválido" });

            // Converte pra stream
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(imagemLimpa);
            }
            catch
            {
                return BadRequest(new { mensagem = "Base64 inválido" });
            }

            using var stream = new MemoryStream(bytes);

            // 🆕 Upload pro R2
            var url = await _r2Service.UploadAsync(stream, extensao, contentType);

            var usuarioId = GetUsuarioId();
            _logger.LogInformation($"Upload R2 realizado pelo usuário {usuarioId}: {url}");

            return Ok(new { url });
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
            if (base64.Any(c => !char.IsLetterOrDigit(c) && c != '+' && c != '/' && c != '='))
                return false;

            if (base64.Length % 4 != 0)
                return false;

            Convert.FromBase64String(base64);
            return true;
        }
        catch
        {
            return false;
        }
    }
}