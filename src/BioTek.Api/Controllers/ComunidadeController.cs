using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BioTek.Api.Controllers;
using BioTek.Api.Models;

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

    [HttpPost("{id}/curtir")]
    public async Task<IActionResult> Curtir(string id, [FromBody] CurtirRequest request)
    {
        await _comunidadeService.CurtirAsync(
            id, 
            GetUsuarioId(), 
            request.TipoAtividade,
            request.IdUsuarioDono,
            request.NomeCurtidor,
            request.TituloLivro
        );
        return Ok(new { mensagem = "Curtida registrada!" });
    }

    [HttpDelete("{id}/curtir")]
    public async Task<IActionResult> Descurtir(string id)
    {
        await _comunidadeService.DescurtirAsync(id, GetUsuarioId());
        return Ok(new { mensagem = "Curtida removida!" });
    }

}