using System.Security.Claims;
using System.Text;
using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
    private readonly IRepository<Acesso> _acessoRepository;

    // 🆕 Emails para ignorar nas estatísticas (admins/testes)
    private static readonly string[] EmailsIgnorados = new[]
    {
        "g.britoalves888@gmail.com",
        "xxxbielxxx@gmail.com"
    };

    public AdminController(
        IRepository<ComentarioLivro> comentarioRepository,
        IRepository<Usuario> usuarioRepository,
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Curtida> curtidaRepository,
        IRepository<Seguidor> seguidorRepository,
        IRepository<Notificacao> notificacaoRepository,
        IRepository<Acesso> acessoRepository)
    {
        _comentarioRepository = comentarioRepository;
        _usuarioRepository = usuarioRepository;
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _curtidaRepository = curtidaRepository;
        _seguidorRepository = seguidorRepository;
        _notificacaoRepository = notificacaoRepository;
        _acessoRepository = acessoRepository;
    }

    private string GetUsuarioId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value!;

    private async Task<bool> IsAdmin()
    {
        var usuario = await _usuarioRepository.GetByIdAsync(GetUsuarioId());
        return usuario?.Adm == true;
    }

    // 🆕 Obter IDs dos usuários ignorados
    private async Task<HashSet<string>> GetIdsIgnorados()
    {
        var usuarios = await _usuarioRepository.GetAllAsync();
        return usuarios
            .Where(u => EmailsIgnorados.Contains(u.Email))
            .Select(u => u.Id)
            .ToHashSet();
    }

    // ===== DASHBOARD =====
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] DateTime? dataInicio = null, [FromQuery] DateTime? dataFim = null)
    {
        if (!await IsAdmin()) return Forbid();

        var idsIgnorados = await GetIdsIgnorados(); // 🆕

        var usuarios = (await _usuarioRepository.GetAllAsync())
            .Where(u => !idsIgnorados.Contains(u.Id)); // 🆕 Filtra admins
        var comentarios = await _comentarioRepository.GetAllAsync();
        var livros = await _livroRepository.GetAllAsync();
        var acessos = (await _acessoRepository.GetAllAsync())
            .Where(a => !idsIgnorados.Contains(a.IdUsuario)); // 🆕 Filtra acessos dos admins

        if (dataInicio.HasValue)
        {
            usuarios = usuarios.Where(u => u.DataCriacao >= dataInicio.Value);
            comentarios = comentarios.Where(c => c.DataCriacao >= dataInicio.Value);
            livros = livros.Where(l => l.DataCriacao >= dataInicio.Value);
            acessos = acessos.Where(a => a.DataCriacao >= dataInicio.Value);
        }
        if (dataFim.HasValue)
        {
            var fim = dataFim.Value.AddDays(1);
            usuarios = usuarios.Where(u => u.DataCriacao < fim);
            comentarios = comentarios.Where(c => c.DataCriacao < fim);
            livros = livros.Where(l => l.DataCriacao < fim);
            acessos = acessos.Where(a => a.DataCriacao < fim);
        }

        var agora = DateTime.UtcNow;
        var seteDias = agora.AddDays(-7);
        var trintaDias = agora.AddDays(-30);

        return Ok(new
        {
            totalUsuarios = usuarios.Count(),
            usuariosAtivos = usuarios.Count(u => u.Ativo),
            totalComentarios = comentarios.Count(),
            comentariosFlagged = comentarios.Count(c => c.Flagged),
            totalLivros = livros.Count(),
            totalAcessos = acessos.Count(),
            acessos7Dias = acessos.Count(a => a.DataCriacao >= seteDias),
            acessos30Dias = acessos.Count(a => a.DataCriacao >= trintaDias),
            mediaAcessosPorUsuario = usuarios.Any() ? Math.Round(usuarios.Average(u => (double)u.TotalAcessos), 1) : 0,
            usuariosAtivos30Dias = acessos.Where(a => a.DataCriacao >= trintaDias).Select(a => a.IdUsuario).Distinct().Count()
        });
    }

    // ===== USUÁRIOS =====
    [HttpGet("usuarios")]
    public async Task<IActionResult> GetUsuarios(
        [FromQuery] int pagina = 1, 
        [FromQuery] int limite = 20,
        [FromQuery] string? ordenar = null,
        [FromQuery] string? status = null)
    {
        if (!await IsAdmin()) return Forbid();

        var usuarios = (await _usuarioRepository.GetAllAsync()).AsEnumerable();
        var livros = await _livroRepository.GetAllAsync();
        var acessos = await _acessoRepository.GetAllAsync();
        var agora = DateTime.UtcNow;
        var trintaDias = agora.AddDays(-30);

        switch (status)
        {
            case "ativos":
                usuarios = usuarios.Where(u => u.Ativo);
                break;
            case "desativados":
                usuarios = usuarios.Where(u => !u.Ativo);
                break;
            case "inativos30d":
                usuarios = usuarios.Where(u => !u.UltimoAcesso.HasValue || u.UltimoAcesso < trintaDias);
                break;
        }

        switch (ordenar)
        {
            case "recentes":
                usuarios = usuarios.OrderByDescending(u => u.UltimoAcesso ?? DateTime.MinValue);
                break;
            case "cadastro":
                usuarios = usuarios.OrderByDescending(u => u.DataCriacao);
                break;
            default:
                usuarios = usuarios.OrderByDescending(u => u.TotalAcessos);
                break;
        }

        var total = usuarios.Count();
        var totalPaginas = (int)Math.Ceiling(total / (double)limite);

        var resultado = usuarios
            .Skip((pagina - 1) * limite)
            .Take(limite)
            .Select(u => new
            {
                u.Id, u.Nome, u.Nickname, u.Email, u.Ativo, u.Adm, u.DataCriacao,
                u.QuantidadeLivros, u.QuantidadeLivrosLidos,
                u.TotalAcessos,
                u.UltimoAcesso,
                u.AceitouTermos,
                u.Avatar,
                TotalLivros = livros.Count(l => l.IdUsuario == u.Id),
                TotalLivrosLidos = livros.Count(l => l.IdUsuario == u.Id && l.Status == StatusLeitura.Lido),
                Acessos30Dias = acessos.Count(a => a.IdUsuario == u.Id && a.DataCriacao >= trintaDias)
            })
            .ToList();

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
            usuario.MediaAvaliacoes,
            usuario.AceitouTermos,
            usuario.TotalAcessos,
            usuario.UltimoAcesso
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

        var livros = await _livroRepository.FindAsync(l => l.IdUsuario == id);
        foreach (var livro in livros)
            await _livroRepository.DeleteAsync(livro.Id);

        var avaliacoes = await _avaliacaoRepository.FindAsync(a => a.IdUsuario == id);
        foreach (var av in avaliacoes)
            await _avaliacaoRepository.DeleteAsync(av.Id);

        var comentarios = await _comentarioRepository.FindAsync(c => c.IdUsuario == id);
        foreach (var c in comentarios)
            await _comentarioRepository.DeleteAsync(c.Id);

        var curtidas = await _curtidaRepository.FindAsync(c => c.IdUsuario == id);
        foreach (var c in curtidas)
            await _curtidaRepository.DeleteAsync(c.Id);

        var seguidores = await _seguidorRepository.FindAsync(s => s.IdUsuario == id || s.IdSeguidor == id);
        foreach (var s in seguidores)
            await _seguidorRepository.DeleteAsync(s.Id);

        var notificacoes = await _notificacaoRepository.FindAsync(n => n.IdUsuario == id);
        foreach (var n in notificacoes)
            await _notificacaoRepository.DeleteAsync(n.Id);

        await _usuarioRepository.DeleteAsync(id);

        return NoContent();
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
            })
            .ToList();

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

    // ===== LIVROS =====
    [HttpGet("livros")]
    public async Task<IActionResult> GetLivros([FromQuery] int pagina = 1, [FromQuery] int limite = 20)
    {
        if (!await IsAdmin()) return Forbid();

        var livros = await _livroRepository.GetAllAsync();
        var usuarios = await _usuarioRepository.GetAllAsync();
        var usuarioMap = usuarios.ToDictionary(u => u.Id, u => u);

        var query = livros.OrderByDescending(l => l.DataCriacao);
        var total = query.Count();
        var totalPaginas = (int)Math.Ceiling(total / (double)limite);

        var resultado = query
            .Skip((pagina - 1) * limite)
            .Take(limite)
            .Select(l => new
            {
                l.Id, l.Titulo, l.Autor, l.Imagem, l.Status, l.DataCriacao,
                Usuario = usuarioMap.TryGetValue(l.IdUsuario, out var u) ? new { u.Id, u.Nome, u.Nickname } : null
            })
            .ToList();

        return Ok(new { dados = resultado, total, totalPaginas, pagina });
    }

    [HttpDelete("livros/{id}")]
    public async Task<IActionResult> ExcluirLivro(string id)
    {
        if (!await IsAdmin()) return Forbid();
        await _livroRepository.DeleteAsync(id);
        return NoContent();
    }

    // ===== EXPORTAR =====
    [HttpGet("exportar")]
    public async Task<IActionResult> ExportarDados()
    {
        if (!await IsAdmin()) return Forbid();

        var idsIgnorados = await GetIdsIgnorados(); // 🆕

        var usuarios = (await _usuarioRepository.GetAllAsync())
            .Where(u => !idsIgnorados.Contains(u.Id)); // 🆕 Filtra admins
        var livros = await _livroRepository.GetAllAsync();
        var acessos = (await _acessoRepository.GetAllAsync())
            .Where(a => !idsIgnorados.Contains(a.IdUsuario)); // 🆕 Filtra acessos dos admins
        var comentarios = await _comentarioRepository.GetAllAsync();
        var agora = DateTime.UtcNow;
        var trintaDias = agora.AddDays(-30);

        var sb = new StringBuilder();
        sb.AppendLine("Nome,Nickname,Email,Admin,Ativo,Cadastro,Último Acesso,Total Acessos,Acessos 30d,Livros,Livros Lidos,Aceitou Termos");

        foreach (var u in usuarios.OrderByDescending(u => u.TotalAcessos))
        {
            var livrosUsuario = livros.Where(l => l.IdUsuario == u.Id).ToList();
            var totalLivros = livrosUsuario.Count;
            var livrosLidos = livrosUsuario.Count(l => l.Status == StatusLeitura.Lido);
            var acessos30Dias = acessos.Count(a => a.IdUsuario == u.Id && a.DataCriacao >= trintaDias);
            var ultimoAcesso = u.UltimoAcesso.HasValue ? u.UltimoAcesso.Value.ToString("yyyy-MM-dd HH:mm") : "Nunca";

            sb.AppendLine($"{EscapeCsv(u.Nome)},{EscapeCsv(u.Nickname)},{u.Email},{u.Adm},{u.Ativo},{u.DataCriacao:yyyy-MM-dd HH:mm},{ultimoAcesso},{u.TotalAcessos},{acessos30Dias},{totalLivros},{livrosLidos},{u.AceitouTermos}");
        }

        sb.AppendLine();
        sb.AppendLine("--- RESUMO ---");
        sb.AppendLine($"Total Usuários,{usuarios.Count()}");
        sb.AppendLine($"Usuários Ativos,{usuarios.Count(u => u.Ativo)}");
        sb.AppendLine($"Total Acessos,{acessos.Count()}");
        sb.AppendLine($"Acessos 7 dias,{acessos.Count(a => a.DataCriacao >= agora.AddDays(-7))}");
        sb.AppendLine($"Acessos 30 dias,{acessos.Count(a => a.DataCriacao >= trintaDias)}");
        sb.AppendLine($"Usuários Ativos 30d,{acessos.Where(a => a.DataCriacao >= trintaDias).Select(a => a.IdUsuario).Distinct().Count()}");
        sb.AppendLine($"Total Livros,{livros.Count()}");
        sb.AppendLine($"Total Comentários,{comentarios.Count()}");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"bliteca-relatorio-{DateTime.Now:yyyy-MM-dd}.csv");
    }

    private string EscapeCsv(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        return texto.Contains(',') || texto.Contains('"') ? $"\"{texto.Replace("\"", "\"\"")}\"" : texto;
    }

    [HttpGet("health")]
    [HttpHead("health")]
    [AllowAnonymous]
    public IActionResult Get()
    {
        return Ok(new { 
            status = "ok", 
            timestamp = DateTime.UtcNow 
        });
    }
}