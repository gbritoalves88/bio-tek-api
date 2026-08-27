using System.Security.Claims;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value!;
    }

    [HttpGet]
    public async Task<IActionResult> GetStreak()
    {
        var streak = await _streakService.GetStreakAsync(GetUsuarioId());
        return Ok(streak);
    }

    [HttpGet("historico")]
    public async Task<IActionResult> GetHistorico()
    {
        var historico = await _streakService.GetHistoricoAsync(GetUsuarioId());
        return Ok(historico);
    }

    [HttpGet("metricas-mes")]
    public async Task<IActionResult> GetMetricasMes()
    {
        var metricas = await _streakService.GetMetricasMesAsync(GetUsuarioId());
        return Ok(metricas);
    }

    // 🆕 ENDPOINTS PÚBLICOS PARA VISUALIZAÇÃO
    [AllowAnonymous]
    [HttpGet("usuario/{usuarioId}")]
    public async Task<IActionResult> GetStreakPorUsuario(string usuarioId)
    {
        var streak = await _streakService.GetStreakAsync(usuarioId);
        return Ok(streak);
    }

    [AllowAnonymous]
    [HttpGet("usuario/{usuarioId}/historico")]
    public async Task<IActionResult> GetHistoricoPorUsuario(string usuarioId)
    {
        var historico = await _streakService.GetHistoricoAsync(usuarioId);
        return Ok(historico);
    }

    [AllowAnonymous]
    [HttpGet("usuario/{usuarioId}/metricas-mes")]
    public async Task<IActionResult> GetMetricasMesPorUsuario(string usuarioId)
    {
        var metricas = await _streakService.GetMetricasMesAsync(usuarioId);
        return Ok(metricas);
    }
}