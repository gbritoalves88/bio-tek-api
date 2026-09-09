// BioTek.Api/Controllers/ComentarioLivroController.cs

using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ComentarioLivroController : ControllerBase
{
    private readonly IComentarioLivroService _comentarioService;
    private readonly ILivroService _livroService;
    private readonly IUsuarioService _usuarioService;
    private readonly INotificacaoService _notificacaoService;

    public ComentarioLivroController(
        IComentarioLivroService comentarioService,
        ILivroService livroService,
        IUsuarioService usuarioService,
        INotificacaoService notificacaoService)
    {
        _comentarioService = comentarioService;
        _livroService = livroService;
        _usuarioService = usuarioService;
        _notificacaoService = notificacaoService;
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

    [HttpGet("livro/{idLivro}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPorLivro(string idLivro)
    {
        var comentarios = await _comentarioService.GetPorLivroAsync(idLivro);
        return Ok(comentarios);
    }

    [HttpPost]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Criar([FromBody] ComentarioLivro comentario)
    {
        try
        {
            var usuarioId = GetUsuarioId();
            
            // 🆕 Validação básica
            if (string.IsNullOrWhiteSpace(comentario.Comentario))
                return BadRequest(new { mensagem = "Comentário é obrigatório" });
            
            if (string.IsNullOrWhiteSpace(comentario.IdLivro))
                return BadRequest(new { mensagem = "IdLivro é obrigatório" });
            
            comentario.IdUsuario = usuarioId;
            
            var novo = await _comentarioService.CriarAsync(comentario);
            
            // Dispara notificação (apenas se o livro existe e não é do próprio usuário)
            var livro = await _livroService.GetPorIdAsync(comentario.IdLivro);
            
            if (livro != null && livro.IdUsuario != usuarioId)
            {
                var usuario = await _usuarioService.GetPorIdAsync(usuarioId);
                await _notificacaoService.CriarNotificacaoComentario(
                    livro.IdUsuario,
                    livro.Id,
                    livro.Titulo,
                    usuario?.Nome ?? "Alguém"
                );
            }
            
            return CreatedAtAction(nameof(GetPorLivro), new { idLivro = novo.IdLivro }, novo);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { mensagem = "Usuário não autenticado" });
        }
    }

    [HttpDelete("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Deletar(string id)
    {
        try
        {
            var result = await _comentarioService.DeletarAsync(id, GetUsuarioId());
            return result ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { mensagem = "Usuário não autenticado" });
        }
    }
}