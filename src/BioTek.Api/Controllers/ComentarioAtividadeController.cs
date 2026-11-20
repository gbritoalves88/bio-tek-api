// BioTek.Api/Controllers/ComentarioAtividadeController.cs
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
public class ComentarioAtividadeController : ControllerBase
{
    private readonly IComentarioAtividadeService _service;
    private readonly IComentarioLivroService _comentarioLivroService;
    private readonly IUsuarioService _usuarioService;
    private readonly INotificacaoService _notificacaoService;
    private readonly ILivroService _livroService;


    public ComentarioAtividadeController(
    IComentarioAtividadeService service,
    IComentarioLivroService comentarioLivroService,
    IUsuarioService usuarioService,
    INotificacaoService notificacaoService,
    ILivroService livroService)
    {
        _service = service;
        _comentarioLivroService = comentarioLivroService;
        _usuarioService = usuarioService;
        _notificacaoService = notificacaoService;
        _livroService = livroService;
    }

    private string GetUsuarioId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                 ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("Usuário não autenticado");
        return id;
    }

    [HttpGet("atividade/{idAtividade}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPorAtividade(string idAtividade)
    {
        // 🆕 Usa o service correto (ComentarioAtividade, não ComentarioLivro)
        var comentarios = await _service.GetPorAtividadeAsync(idAtividade);
        
        // 🆕 Enriquece com dados do usuário
        var usuariosIds = comentarios.Select(c => c.IdUsuario).ToHashSet();
        var usuarios = await _usuarioService.FindAsync(u => usuariosIds.Contains(u.Id));
        var usuariosMap = usuarios.ToDictionary(u => u.Id);

        var resultado = comentarios.Select(c => new
        {
            c.Id,
            c.IdUsuario,
            c.Comentario,
            c.DataCriacao,
            Usuario = usuariosMap.TryGetValue(c.IdUsuario, out var u)
                ? new { u.Id, u.Nome, u.Nickname, u.Avatar }
                : null
        });

        return Ok(resultado);
    }

    
    [HttpPost]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Criar([FromBody] ComentarioAtividade comentario)
    {
        try
        {
            var usuarioId = GetUsuarioId();
            comentario.IdUsuario = usuarioId;

            var novo = await _service.CriarAsync(comentario);

            // 🆕 Notifica o dono da atividade (o ComentarioLivro original)
            var atividadeOriginal = await _comentarioLivroService.GetPorIdAsync(comentario.IdAtividade);
            if (atividadeOriginal != null && atividadeOriginal.IdUsuario != usuarioId)
            {
                var livro = await _livroService.GetPorIdAsync(atividadeOriginal.IdLivro);
                var usuario = await _usuarioService.GetPorIdAsync(usuarioId);

                await _notificacaoService.CriarNotificacaoComentario(
                    atividadeOriginal.IdUsuario,
                    atividadeOriginal.IdLivro,
                    livro?.Titulo ?? "Livro",
                    usuario?.Nome ?? "Alguém"
                );
            }

            return Ok(novo);
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
        var result = await _service.DeletarAsync(id, GetUsuarioId());
        return result ? NoContent() : NotFound();
    }
}