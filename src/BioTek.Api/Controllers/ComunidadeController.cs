using System.Security.Claims;
using BioTek.Api.Models;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BioTek.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ComunidadeController : ControllerBase
{
    private readonly IComunidadeService _comunidadeService;

    public ComunidadeController(IComunidadeService comunidadeService)
    {
        _comunidadeService = comunidadeService;
    }

    // 🆕 GetUsuarioId melhorado
    private string GetUsuarioId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                 ?? User.FindFirst("sub")?.Value;
        
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("Usuário não autenticado");
        
        return id;
    }

    /// <summary>
    /// Feed da comunidade (público, mas personalizado se autenticado)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("feed")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetFeed([FromQuery] int limite = 30)
    {
        // Validação do limite
        if (limite <= 0 || limite > 50)
            return BadRequest(new { mensagem = "Limite deve estar entre 1 e 50" });

        var usuarioId = User.Identity?.IsAuthenticated == true ? GetUsuarioId() : null;
        var atividades = await _comunidadeService.GetFeedAsync(usuarioId, limite);
        return Ok(atividades);
    }

    /// <summary>
    /// Curtir uma atividade
    /// </summary>
    [Authorize]
    [HttpPost("{id}/curtir")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Curtir(string id, [FromBody] CurtirRequest request)
    {
        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID da atividade é obrigatório" });

        if (string.IsNullOrEmpty(request?.TipoAtividade))
            return BadRequest(new { mensagem = "Tipo de atividade é obrigatório" });

        var usuarioId = GetUsuarioId();
        await _comunidadeService.CurtirAsync(
            id, 
            usuarioId, 
            request.TipoAtividade,
            request.IdUsuarioDono,
            request.NomeCurtidor,
            request.TituloLivro
        );
        return Ok(new { mensagem = "Curtida registrada!" });
    }

    /// <summary>
    /// Remover curtida de uma atividade
    /// </summary>
    [Authorize]
    [HttpDelete("{id}/curtir")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Descurtir(string id)
    {
        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID da atividade é obrigatório" });

        var usuarioId = GetUsuarioId();
        await _comunidadeService.DescurtirAsync(id, usuarioId);
        return Ok(new { mensagem = "Curtida removida!" });
    }
}