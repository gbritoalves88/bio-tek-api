using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ComentarioColecaoController : ControllerBase
{
    private readonly IComentarioColecaoService _comentarioService;

    public ComentarioColecaoController(IComentarioColecaoService comentarioService)
    {
        _comentarioService = comentarioService;
    }

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value!;
    }

    /// <summary>
    /// Comentários de uma coleção
    /// </summary>
    [HttpGet("colecao/{colecaoId}")]
    public async Task<IActionResult> GetPorColecao(string colecaoId)
    {
        var comentarios = await _comentarioService.GetPorColecaoAsync(colecaoId);
        return Ok(comentarios);
    }

    /// <summary>
    /// Cria comentário em coleção
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] ComentarioColecao comentario)
    {
        comentario.IdUsuario = GetUsuarioId();
        var novo = await _comentarioService.CriarAsync(comentario);
        return CreatedAtAction(nameof(GetPorColecao), new { colecaoId = novo.IdColecao }, novo);
    }

    /// <summary>
    /// Remove comentário
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Deletar(string id)
    {
        var result = await _comentarioService.DeletarAsync(id);
        return result ? NoContent() : NotFound();
    }
}