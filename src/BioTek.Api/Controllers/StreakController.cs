using System.Security.Claims;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class StreakController : ControllerBase
{
    private readonly IStreakService _streakService;

    public StreakController(IStreakService streakService)
    {
        _streakService = streakService;
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

    [HttpGet]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetStreak()
    {
        try
        {
            var streak = await _streakService.GetStreakAsync(GetUsuarioId());
            return Ok(streak);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { mensagem = "Usuário não autenticado" });
        }
    }

    [HttpGet("historico")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetHistorico()
    {
        var historico = await _streakService.GetHistoricoAsync(GetUsuarioId());
        return Ok(historico);
    }

    [HttpGet("metricas-mes")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetMetricasMes()
    {
        var metricas = await _streakService.GetMetricasMesAsync(GetUsuarioId());
        return Ok(metricas);
    }

    [AllowAnonymous]
    [HttpGet("usuario/{usuarioId}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetStreakPorUsuario(string usuarioId)
    {
        var streak = await _streakService.GetStreakAsync(usuarioId);
        return Ok(streak);
    }

    [AllowAnonymous]
    [HttpGet("usuario/{usuarioId}/historico")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetHistoricoPorUsuario(string usuarioId)
    {
        var historico = await _streakService.GetHistoricoAsync(usuarioId);
        return Ok(historico);
    }

    [AllowAnonymous]
    [HttpGet("usuario/{usuarioId}/metricas-mes")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetMetricasMesPorUsuario(string usuarioId)
    {
        var metricas = await _streakService.GetMetricasMesAsync(usuarioId);
        return Ok(metricas);
    }
}