using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ComentarioLivroController : ControllerBase
{
    private readonly IComentarioLivroService _comentarioService;

    public ComentarioLivroController(IComentarioLivroService comentarioService)
    {
        _comentarioService = comentarioService;
    }

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value!;
    }

    [HttpGet("livro/{idLivro}")]
    public async Task<IActionResult> GetPorLivro(string idLivro)
    {
        var comentarios = await _comentarioService.GetPorLivroAsync(idLivro);
        return Ok(comentarios);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] ComentarioLivro comentario)
    {
        comentario.IdUsuario = GetUsuarioId();
        var novo = await _comentarioService.CriarAsync(comentario);
        return CreatedAtAction(nameof(GetPorLivro), new { idLivro = novo.IdLivro }, novo);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deletar(string id)
    {
        var result = await _comentarioService.DeletarAsync(id);
        return result ? NoContent() : NotFound();
    }
}