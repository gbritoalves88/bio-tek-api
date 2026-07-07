using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BioTek.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly IRepository<ComentarioLivro> _comentarioRepository;
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;
    private readonly IRepository<Curtida> _curtidaRepository;
    private readonly IRepository<Seguidor> _seguidorRepository;
    private readonly IRepository<Notificacao> _notificacaoRepository;

    public AdminController(
        IRepository<ComentarioLivro> comentarioRepository,
        IRepository<Usuario> usuarioRepository,
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Curtida> curtidaRepository,
        IRepository<Seguidor> seguidorRepository,
        IRepository<Notificacao> notificacaoRepository)
    {
        _comentarioRepository = comentarioRepository;
        _usuarioRepository = usuarioRepository;
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _curtidaRepository = curtidaRepository;
        _seguidorRepository = seguidorRepository;
        _notificacaoRepository = notificacaoRepository;
    }

    private string GetUsuarioId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value!;

    private async Task<bool> IsAdmin()
    {
        var usuario = await _usuarioRepository.GetByIdAsync(GetUsuarioId());
        return usuario?.Adm == true;
    }

    // ===== DASHBOARD =====
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        if (!await IsAdmin()) return Forbid();

        var usuarios = await _usuarioRepository.GetAllAsync();
        var comentarios = await _comentarioRepository.GetAllAsync();
        var livros = await _livroRepository.GetAllAsync();

        return Ok(new
        {
            totalUsuarios = usuarios.Count(),
            usuariosAtivos = usuarios.Count(u => u.Ativo),
            totalComentarios = comentarios.Count(),
            comentariosFlagged = comentarios.Count(c => c.Flagged),
            totalLivros = livros.Count()
        });
    }

    // ===== COMENTÁRIOS =====
    [HttpGet("comentarios")]
    public async Task<IActionResult> GetComentarios([FromQuery] int pagina = 1, [FromQuery] int limite = 20)
    {
        if (!await IsAdmin()) return Forbid();

        var comentarios = await _comentarioRepository.GetAllAsync();
        var usuarios = await _usuarioRepository.GetAllAsync();
        var usuarioMap = usuarios.ToDictionary(u => u.Id, u => u);

        var query = comentarios.OrderByDescending(c => c.DataCriacao);
        var total = query.Count();
        var totalPaginas = (int)Math.Ceiling(total / (double)limite);

        var resultado = query
            .Skip((pagina - 1) * limite)
            .Take(limite)
            .Select(c => new
            {
                c.Id,
                c.IdLivro,
                c.IdUsuario,
                c.Comentario,
                c.DataCriacao,
                c.Flagged,
                c.ToxicidadeScore,
                Usuario = usuarioMap.TryGetValue(c.IdUsuario, out var u) ? new
                {
                    u.Id,
                    u.Nome,
                    u.Nickname
                } : null
            });

        return Ok(new { dados = resultado, total, totalPaginas, pagina });
    }

    [HttpDelete("comentarios/{id}")]
    public async Task<IActionResult> DeletarComentario(string id)
    {
        if (!await IsAdmin()) return Forbid();
        await _comentarioRepository.DeleteAsync(id);
        return NoContent();
    }

    [HttpPut("comentarios/{id}/aprovar")]
    public async Task<IActionResult> AprovarComentario(string id)
    {
        if (!await IsAdmin()) return Forbid();

        var comentario = await _comentarioRepository.GetByIdAsync(id);
        if (comentario == null) return NotFound();

        comentario.Flagged = false;
        comentario.ToxicidadeScore = null;
        await _comentarioRepository.UpdateAsync(id, comentario);

        return Ok(comentario);
    }

    // ===== USUÁRIOS =====
    [HttpGet("usuarios")]
    public async Task<IActionResult> GetUsuarios([FromQuery] int pagina = 1, [FromQuery] int limite = 20)
    {
        if (!await IsAdmin()) return Forbid();

        var usuarios = await _usuarioRepository.GetAllAsync();
        var query = usuarios.OrderByDescending(u => u.DataCriacao);
        var total = query.Count();
        var totalPaginas = (int)Math.Ceiling(total / (double)limite);

        var resultado = query
            .Skip((pagina - 1) * limite)
            .Take(limite)
            .Select(u => new
            {
                u.Id,
                u.Nome,
                u.Nickname,
                u.Email,
                u.Ativo,
                u.Adm,
                u.DataCriacao,
                u.QuantidadeLivros,
                u.QuantidadeLivrosLidos
            });

        return Ok(new { dados = resultado, total, totalPaginas, pagina });
    }

    [HttpGet("usuarios/{id}")]
    public async Task<IActionResult> GetUsuario(string id)
    {
        if (!await IsAdmin()) return Forbid();

        var usuario = await _usuarioRepository.GetByIdAsync(id);
        if (usuario == null) return NotFound();

        return Ok(new
        {
            usuario.Id,
            usuario.Nome,
            usuario.Nickname,
            usuario.Email,
            usuario.Descricao,
            usuario.Avatar,
            usuario.Ativo,
            usuario.Adm,
            usuario.Tema,
            usuario.DataCriacao,
            usuario.DataAtualizacao,
            usuario.QuantidadeLivros,
            usuario.QuantidadeLivrosLidos,
            usuario.MediaAvaliacoes
        });
    }

    [HttpPut("usuarios/{id}/ativo")]
    public async Task<IActionResult> ToggleAtivo(string id)
    {
        if (!await IsAdmin()) return Forbid();

        var usuario = await _usuarioRepository.GetByIdAsync(id);
        if (usuario == null) return NotFound();

        usuario.Ativo = !usuario.Ativo;
        usuario.DataAtualizacao = DateTime.UtcNow;
        await _usuarioRepository.UpdateAsync(id, usuario);

        return Ok(new { usuario.Id, usuario.Ativo, usuario.Nome });
    }

    [HttpDelete("usuarios/{id}")]
    public async Task<IActionResult> ExcluirConta(string id)
    {
        if (!await IsAdmin()) return Forbid();

        if (id == GetUsuarioId())
            return BadRequest(new { mensagem = "Você não pode excluir sua própria conta" });

        // 1. Deletar livros do usuário
        var livros = await _livroRepository.FindAsync(l => l.IdUsuario == id);
        foreach (var livro in livros)
            await _livroRepository.DeleteAsync(livro.Id);

        // 2. Deletar avaliações do usuário
        var avaliacoes = await _avaliacaoRepository.FindAsync(a => a.IdUsuario == id);
        foreach (var av in avaliacoes)
            await _avaliacaoRepository.DeleteAsync(av.Id);

        // 3. Deletar comentários do usuário
        var comentarios = await _comentarioRepository.FindAsync(c => c.IdUsuario == id);
        foreach (var c in comentarios)
            await _comentarioRepository.DeleteAsync(c.Id);

        // 4. Deletar curtidas do usuário
        var curtidas = await _curtidaRepository.FindAsync(c => c.IdUsuario == id);
        foreach (var c in curtidas)
            await _curtidaRepository.DeleteAsync(c.Id);

        // 5. Deletar seguidores/seguindo
        var seguidores = await _seguidorRepository.FindAsync(s => s.IdUsuario == id || s.IdSeguidor == id);
        foreach (var s in seguidores)
            await _seguidorRepository.DeleteAsync(s.Id);

        // 6. Deletar notificações
        var notificacoes = await _notificacaoRepository.FindAsync(n => n.IdUsuario == id);
        foreach (var n in notificacoes)
            await _notificacaoRepository.DeleteAsync(n.Id);

        // 7. Deletar o usuário
        await _usuarioRepository.DeleteAsync(id);

        return NoContent();
    }
}