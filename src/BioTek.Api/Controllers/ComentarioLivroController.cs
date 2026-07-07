// BioTek.Api/Controllers/ComentarioLivroController.cs

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
    private readonly ILivroService _livroService;           // ⬅️ Adicionar
    private readonly IUsuarioService _usuarioService;       // ⬅️ Adicionar
    private readonly INotificacaoService _notificacaoService; // ⬅️ Adicionar

    public ComentarioLivroController(
        IComentarioLivroService comentarioService,
        ILivroService livroService,                         // ⬅️ Adicionar
        IUsuarioService usuarioService,                     // ⬅️ Adicionar
        INotificacaoService notificacaoService)             // ⬅️ Adicionar
    {
        _comentarioService = comentarioService;
        _livroService = livroService;
        _usuarioService = usuarioService;
        _notificacaoService = notificacaoService;
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
        var usuarioId = GetUsuarioId();
        comentario.IdUsuario = usuarioId;
        
        var novo = await _comentarioService.CriarAsync(comentario);
        
        // ⬇️ DISPARAR NOTIFICAÇÃO AQUI ⬇️
        var livro = await _livroService.GetPorIdAsync(comentario.IdLivro);
        
        if (livro != null && livro.IdUsuario != usuarioId)
        {
            var usuario = await _usuarioService.GetPorIdAsync(usuarioId);
            await _notificacaoService.CriarNotificacaoComentario(
                livro.IdUsuario,              // Dono do livro (quem recebe)
                livro.Id,                     // ID do livro
                livro.Titulo,                 // Título do livro
                usuario?.Nome ?? "Alguém"     // Nome de quem comentou
            );
        }
        
        return CreatedAtAction(nameof(GetPorLivro), new { idLivro = novo.IdLivro }, novo);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deletar(string id)
    {
        var result = await _comentarioService.DeletarAsync(id, GetUsuarioId());
        return result ? NoContent() : NotFound();
    }
}