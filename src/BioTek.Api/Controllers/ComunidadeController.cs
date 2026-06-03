using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ComunidadeController : ControllerBase
{
    private readonly IComunidadeService _comunidadeService;

    public ComunidadeController(IComunidadeService comunidadeService)
    {
        _comunidadeService = comunidadeService;
    }

     private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value!;
    }

    [AllowAnonymous]
    [HttpGet("feed")]
    public async Task<IActionResult> GetFeed([FromQuery] int limite = 30)
    {
        var atividades = await _comunidadeService.GetFeedAsync(GetUsuarioId(), limite);
        return Ok(atividades);
    }

}