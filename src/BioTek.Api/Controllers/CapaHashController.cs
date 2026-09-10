using System.Security.Claims;
using BioTek.Api.Models;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BioTek.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CapaHashController : ControllerBase
{
    private readonly ICapaHashService _capaHashService;
    private readonly IUsuarioService _usuarioService;
    private readonly ILogger<CapaHashController> _logger;

    public CapaHashController(
        ICapaHashService capaHashService,
        IUsuarioService usuarioService,
        ILogger<CapaHashController> logger)
    {
        _capaHashService = capaHashService;
        _usuarioService = usuarioService;
        _logger = logger;
    }

    private string GetUsuarioId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                 ?? User.FindFirst("sub")?.Value;
        
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("Usuário não autenticado");
        
        return id;
    }

    private async Task<bool> IsAdminAsync()
    {
        var usuario = await _usuarioService.GetPorIdAsync(GetUsuarioId());
        return usuario?.Adm == true;
    }

    /// <summary>
    /// Popular banco com hashes das capas (ADMIN)
    /// </summary>
    [Authorize]
    [HttpPost("popular")]
    [EnableRateLimiting("AdminPolicy")]
    public async Task<IActionResult> Popular(
        [FromQuery] int? limiteCatalogo = null, 
        [FromQuery] int? limiteLivros = null)
    {
        if (!await IsAdminAsync())
            return Forbid();

        try
        {
            _logger.LogInformation($"Iniciando população de hashes...");
            
            var resultado = await _capaHashService.PopularAsync(limiteCatalogo, limiteLivros);
            
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao popular hashes");
            return StatusCode(500, new { mensagem = "Erro ao popular hashes" });
        }
    }

    /// <summary>
    /// Buscar livro por imagem da capa
    /// </summary>
    [AllowAnonymous]
    [HttpPost("buscar")]
    [EnableRateLimiting("UploadPolicy")]
    public async Task<IActionResult> Buscar([FromBody] BuscarPorCapaRequest request)
    {
        // 🆕 Validar request
        if (request == null || string.IsNullOrEmpty(request.ImagemBase64))
            return BadRequest(new { mensagem = "Imagem é obrigatória" });

        // 🆕 Validar formato
        if (!request.ImagemBase64.StartsWith("data:image/"))
            return BadRequest(new { mensagem = "Formato de imagem inválido" });

        // 🆕 Validar tamanho (máx 5MB)
        if (request.ImagemBase64.Length > 7_000_000)
            return BadRequest(new { mensagem = "Imagem muito grande. Máximo: 5MB" });

        try
        {
            var resultados = await _capaHashService.BuscarPorHashAsync(request.ImagemBase64);
            
            if (resultados == null || resultados.Count == 0)
                return NotFound(new { mensagem = "Nenhum livro encontrado" });

            return Ok(resultados);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar por capa");
            return StatusCode(500, new { mensagem = "Erro ao buscar livro" });
        }
    }

    /// <summary>
    /// Limpar todos os hashes (ADMIN)
    /// </summary>
    [Authorize]
    [HttpDelete("limpar")]
    [EnableRateLimiting("AdminPolicy")]
    public async Task<IActionResult> Limpar()
    {
        if (!await IsAdminAsync())
            return Forbid();

        try
        {
            var total = await _capaHashService.LimparTodosAsync();
            return Ok(new { mensagem = "Limpeza concluída", total });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao limpar hashes");
            return StatusCode(500, new { mensagem = "Erro ao limpar hashes" });
        }
    }
}
