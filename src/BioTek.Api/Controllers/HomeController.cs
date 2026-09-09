using System.Security.Claims;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class HomeController : ControllerBase
{
    private readonly IHomeService _homeService;

    public HomeController(IHomeService homeService)
    {
        _homeService = homeService;
    }

    // 🆕 Método melhorado com fallback e validação
    private string GetUsuarioId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                 ?? User.FindFirst("sub")?.Value;
        
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("Usuário não autenticado");
        
        return id;
    }

    [HttpGet("dashboard")]
    [EnableRateLimiting("GeneralPolicy")] // 🆕 Rate limiting
    public async Task<IActionResult> GetDashboard()
    {
        try
        {
            var usuarioId = GetUsuarioId();
            var dashboard = await _homeService.GetDashboardAsync(usuarioId);
            return Ok(dashboard);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { mensagem = "Usuário não autenticado" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensagem = "Erro interno ao carregar dashboard" });
        }
    }
}