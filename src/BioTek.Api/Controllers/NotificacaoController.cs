using System.Security.Claims;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class NotificacaoController : ControllerBase
{
    private readonly INotificacaoService _notificacaoService;

    public NotificacaoController(INotificacaoService notificacaoService)
    {
        _notificacaoService = notificacaoService;
    }

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    }

    [HttpGet("nao-lidas")]
    public async Task<IActionResult> GetNaoLidas()
    {
        var notificacoes = await _notificacaoService.GetNaoLidasAsync(GetUsuarioId());
        return Ok(notificacoes);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var notificacoes = await _notificacaoService.GetAllAsync(GetUsuarioId());
        return Ok(notificacoes);
    }

    [HttpPost("marcar-lidas")]
    public async Task<IActionResult> MarcarLidas()
    {
        await _notificacaoService.MarcarComoLidasAsync(GetUsuarioId());
        return Ok();
    }
}