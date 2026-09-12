using System.Security.Claims;
using System.Text;
using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BioTek.Application.Services;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("AdminPolicy")]
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
    private readonly IServiceProvider _serviceProvider;

    // 🆕 Cache do dashboard
    private static object? _cacheDashboard;
    private static DateTime _cacheDashboardTime = DateTime.MinValue;
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(5);
    private readonly ILogger<AdminController> _logger;

    // 🆕 Emails para ignorar nas estatísticas (admins/testes)
    private static readonly string[] EmailsIgnorados = new[]
    {
        "g.britoalves888@gmail.com",
        "xxxbielxxx@gmail.com",
        "bliteca888@gmail.com"
    };

    public AdminController(
        IRepository<ComentarioLivro> comentarioRepository,
        IRepository<Usuario> usuarioRepository,
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Curtida> curtidaRepository,
        IRepository<Seguidor> seguidorRepository,
        IRepository<Notificacao> notificacaoRepository,
        IRepository<Acesso> acessoRepository,
        IServiceProvider serviceProvider,
        ILogger<AdminController> logger)
    {
        _comentarioRepository = comentarioRepository;
        _usuarioRepository = usuarioRepository;
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _curtidaRepository = curtidaRepository;
        _seguidorRepository = seguidorRepository;
        _notificacaoRepository = notificacaoRepository;
        _acessoRepository = acessoRepository;
         _serviceProvider = serviceProvider;
         _logger = logger;  // 🆕 Adicionar
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

    // 🆕 Verificação de admin com cache
    private async Task<bool> IsAdmin()
    {
        var usuario = await _usuarioRepository.GetByIdAsync(GetUsuarioId());
        return usuario?.Adm == true;
    }

    // 🆕 Obter IDs dos usuários ignorados com cache
    private async Task<HashSet<string>> GetIdsIgnorados()
    {
        var usuarios = await _usuarioRepository.FindAsync(u => EmailsIgnorados.Contains(u.Email));
        return usuarios.Select(u => u.Id).ToHashSet();
    }

    // ===== DASHBOARD =====
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] DateTime? dataInicio = null, [FromQuery] DateTime? dataFim = null)
    {
        if (!await IsAdmin()) return Forbid();

        // 🆕 Cache do dashboard (sem filtros)
        if (!dataInicio.HasValue && !dataFim.HasValue)
        {
            lock (_cacheLock)
            {
                if (_cacheDashboard != null && DateTime.UtcNow - _cacheDashboardTime < CACHE_DURATION)
                {
                    return Ok(_cacheDashboard);
                }
            }
        }

        var idsIgnorados = await GetIdsIgnorados();

        // 🆕 Buscar dados em paralelo
        var tarefas = new
        {
            Usuarios = _usuarioRepository.GetAllAsync(),
            Comentarios = _comentarioRepository.GetAllAsync(),
            Livros = _livroRepository.GetAllAsync(),
            Acessos = _acessoRepository.GetAllAsync()
        };

        await Task.WhenAll(tarefas.Usuarios, tarefas.Comentarios, tarefas.Livros, tarefas.Acessos);

        var usuarios = (await tarefas.Usuarios).Where(u => !idsIgnorados.Contains(u.Id));
        var comentarios = await tarefas.Comentarios;
        var livros = await tarefas.Livros;
        var acessos = (await tarefas.Acessos).Where(a => !idsIgnorados.Contains(a.IdUsuario));

        // 🆕 Aplicar filtros de data
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

        // 🆕 Materializar listas
        var usuariosLista = usuarios.ToList();
        var acessosLista = acessos.ToList();

        var agora = DateTime.UtcNow;
        var seteDias = agora.AddDays(-7);
        var trintaDias = agora.AddDays(-30);

        var resultado = new
        {
            totalUsuarios = usuariosLista.Count,
            usuariosAtivos = usuariosLista.Count(u => u.Ativo),
            totalComentarios = comentarios.Count(),
            comentariosFlagged = comentarios.Count(c => c.Flagged),
            totalLivros = livros.Count(),
            totalAcessos = acessosLista.Count,
            acessos7Dias = acessosLista.Count(a => a.DataCriacao >= seteDias),
            acessos30Dias = acessosLista.Count(a => a.DataCriacao >= trintaDias),
            mediaAcessosPorUsuario = usuariosLista.Any() 
                ? Math.Round(usuariosLista.Average(u => (double)u.TotalAcessos), 1) 
                : 0,
            usuariosAtivos30Dias = acessosLista
                .Where(a => a.DataCriacao >= trintaDias)
                .Select(a => a.IdUsuario)
                .Distinct()
                .Count()
        };

        // 🆕 Salvar no cache (sem filtros)
        if (!dataInicio.HasValue && !dataFim.HasValue)
        {
            lock (_cacheLock)
            {
                _cacheDashboard = resultado;
                _cacheDashboardTime = DateTime.UtcNow;
            }
        }

        return Ok(resultado);
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

        // 🆕 Validar paginação
        pagina = Math.Max(1, pagina);
        limite = Math.Clamp(limite, 1, 100);

        var usuarios = (await _usuarioRepository.GetAllAsync()).AsEnumerable();
        var livros = await _livroRepository.GetAllAsync();
        var acessos = await _acessoRepository.GetAllAsync();
        var agora = DateTime.UtcNow;
        var trintaDias = agora.AddDays(-30);

        // 🆕 Agrupar dados para evitar N+1
        var livrosPorUsuario = livros
            .GroupBy(l => l.IdUsuario)
            .ToDictionary(g => g.Key, g => g.ToList());

        var acessosPorUsuario = acessos
            .GroupBy(a => a.IdUsuario)
            .ToDictionary(g => g.Key, g => g.ToList());

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

        var usuariosLista = usuarios.ToList();
        var total = usuariosLista.Count;
        var totalPaginas = (int)Math.Ceiling(total / (double)limite);

        var resultado = usuariosLista
            .Skip((pagina - 1) * limite)
            .Take(limite)
            .Select(u =>
            {
                var livrosUsuario = livrosPorUsuario.GetValueOrDefault(u.Id, new List<Livro>());
                var acessosUsuario = acessosPorUsuario.GetValueOrDefault(u.Id, new List<Acesso>());

                return new
                {
                    u.Id, u.Nome, u.Nickname, u.Email, u.Ativo, u.Adm, u.DataCriacao,
                    u.QuantidadeLivros, u.QuantidadeLivrosLidos,
                    u.TotalAcessos,
                    u.UltimoAcesso,
                    u.AceitouTermos,
                    u.Avatar,
                    TotalLivros = livrosUsuario.Count,
                    TotalLivrosLidos = livrosUsuario.Count(l => l.Status == StatusLeitura.Lido),
                    Acessos30Dias = acessosUsuario.Count(a => a.DataCriacao >= trintaDias)
                };
            })
            .ToList();

        return Ok(new { dados = resultado, total, totalPaginas, pagina });
    }

    [HttpGet("usuarios/{id}")]
    public async Task<IActionResult> GetUsuario(string id)
    {
        if (!await IsAdmin()) return Forbid();

        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID é obrigatório" });

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

        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID é obrigatório" });

        var usuario = await _usuarioRepository.GetByIdAsync(id);
        if (usuario == null) return NotFound();

        usuario.Ativo = !usuario.Ativo;
        usuario.DataAtualizacao = DateTime.UtcNow;
        await _usuarioRepository.UpdateAsync(id, usuario);

        // 🆕 Limpar cache do dashboard
        LimparCacheDashboard();

        return Ok(new { usuario.Id, usuario.Ativo, usuario.Nome });
    }

    [HttpDelete("usuarios/{id}")]
    public async Task<IActionResult> ExcluirConta(string id)
    {
        if (!await IsAdmin()) return Forbid();

        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID é obrigatório" });

        if (id == GetUsuarioId())
            return BadRequest(new { mensagem = "Você não pode excluir sua própria conta" });

        // 🆕 Buscar todos os dados em paralelo
        var tarefas = new
        {
            Livros = _livroRepository.FindAsync(l => l.IdUsuario == id),
            Avaliacoes = _avaliacaoRepository.FindAsync(a => a.IdUsuario == id),
            Comentarios = _comentarioRepository.FindAsync(c => c.IdUsuario == id),
            Curtidas = _curtidaRepository.FindAsync(c => c.IdUsuario == id),
            Seguidores = _seguidorRepository.FindAsync(s => s.IdUsuario == id || s.IdSeguidor == id),
            Notificacoes = _notificacaoRepository.FindAsync(n => n.IdUsuario == id)
        };

        await Task.WhenAll(tarefas.Livros, tarefas.Avaliacoes, tarefas.Comentarios, 
            tarefas.Curtidas, tarefas.Seguidores, tarefas.Notificacoes);

        // 🆕 Deletar em paralelo
        var tarefasDelete = new List<Task>();
        
        foreach (var livro in await tarefas.Livros)
            tarefasDelete.Add(_livroRepository.DeleteAsync(livro.Id));
        
        foreach (var av in await tarefas.Avaliacoes)
            tarefasDelete.Add(_avaliacaoRepository.DeleteAsync(av.Id));
        
        foreach (var c in await tarefas.Comentarios)
            tarefasDelete.Add(_comentarioRepository.DeleteAsync(c.Id));
        
        foreach (var c in await tarefas.Curtidas)
            tarefasDelete.Add(_curtidaRepository.DeleteAsync(c.Id));
        
        foreach (var s in await tarefas.Seguidores)
            tarefasDelete.Add(_seguidorRepository.DeleteAsync(s.Id));
        
        foreach (var n in await tarefas.Notificacoes)
            tarefasDelete.Add(_notificacaoRepository.DeleteAsync(n.Id));

        await Task.WhenAll(tarefasDelete);
        await _usuarioRepository.DeleteAsync(id);

        // 🆕 Limpar cache do dashboard
        LimparCacheDashboard();

        return NoContent();
    }

    // ===== COMENTÁRIOS =====
    [HttpGet("comentarios")]
    public async Task<IActionResult> GetComentarios([FromQuery] int pagina = 1, [FromQuery] int limite = 20)
    {
        if (!await IsAdmin()) return Forbid();

        pagina = Math.Max(1, pagina);
        limite = Math.Clamp(limite, 1, 100);

        var comentarios = await _comentarioRepository.GetAllAsync();
        var usuarios = await _usuarioRepository.GetAllAsync();
        var usuarioMap = usuarios.ToDictionary(u => u.Id, u => u);

        var query = comentarios.OrderByDescending(c => c.DataCriacao).ToList();
        var total = query.Count;
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

        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID é obrigatório" });

        await _comentarioRepository.DeleteAsync(id);
        return NoContent();
    }

    [HttpPut("comentarios/{id}/aprovar")]
    public async Task<IActionResult> AprovarComentario(string id)
    {
        if (!await IsAdmin()) return Forbid();

        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID é obrigatório" });

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

        pagina = Math.Max(1, pagina);
        limite = Math.Clamp(limite, 1, 100);

        var livros = await _livroRepository.GetAllAsync();
        var usuarios = await _usuarioRepository.GetAllAsync();
        var usuarioMap = usuarios.ToDictionary(u => u.Id, u => u);

        var query = livros.OrderByDescending(l => l.DataCriacao).ToList();
        var total = query.Count;
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

        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID é obrigatório" });

        await _livroRepository.DeleteAsync(id);
        return NoContent();
    }

    // ===== EXPORTAR =====
    [HttpGet("exportar")]
    public async Task<IActionResult> ExportarDados()
    {
        if (!await IsAdmin()) return Forbid();

        var idsIgnorados = await GetIdsIgnorados();

        var usuarios = (await _usuarioRepository.GetAllAsync())
            .Where(u => !idsIgnorados.Contains(u.Id));
        var livros = await _livroRepository.GetAllAsync();
        var acessos = (await _acessoRepository.GetAllAsync())
            .Where(a => !idsIgnorados.Contains(a.IdUsuario));
        var comentarios = await _comentarioRepository.GetAllAsync();
        var agora = DateTime.UtcNow;
        var trintaDias = agora.AddDays(-30);

        // 🆕 Agrupar para evitar N+1
        var livrosPorUsuario = livros
            .GroupBy(l => l.IdUsuario)
            .ToDictionary(g => g.Key, g => g.ToList());

        var acessosPorUsuario = acessos
            .GroupBy(a => a.IdUsuario)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sb = new StringBuilder();
        sb.AppendLine("Nome,Nickname,Email,Admin,Ativo,Cadastro,Último Acesso,Total Acessos,Acessos 30d,Livros,Livros Lidos,Aceitou Termos");

        foreach (var u in usuarios.OrderByDescending(u => u.TotalAcessos))
        {
            var livrosUsuario = livrosPorUsuario.GetValueOrDefault(u.Id, new List<Livro>());
            var acessosUsuario = acessosPorUsuario.GetValueOrDefault(u.Id, new List<Acesso>());
            var totalLivros = livrosUsuario.Count;
            var livrosLidos = livrosUsuario.Count(l => l.Status == StatusLeitura.Lido);
            var acessos30Dias = acessosUsuario.Count(a => a.DataCriacao >= trintaDias);
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
    [EnableRateLimiting("GeneralPolicy")]
    public IActionResult Get()
    {
        return Ok(new { 
            status = "ok", 
            timestamp = DateTime.UtcNow 
        });
    }

    // 🆕 Limpar cache do dashboard
    private void LimparCacheDashboard()
    {
        lock (_cacheLock)
        {
            _cacheDashboard = null;
            _cacheDashboardTime = DateTime.MinValue;
        }
    }

    [Authorize]
    [HttpPost("manutencao/executar")]
    [EnableRateLimiting("AdminPolicy")]
    public async Task<IActionResult> ExecutarManutencao()
    {
        _logger.LogInformation("🔧 Endpoint manutenção chamado");

        if (!await IsAdmin()) 
        {
            _logger.LogWarning("❌ Não é admin");
            return Forbid();
        }

        _logger.LogInformation("🔧 Manutenção manual solicitada por admin");

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var manutencao = scope.ServiceProvider
                    .GetRequiredService<ManutencaoNoturnaBackgroundService>();
                
                await manutencao.ExecutarManualmenteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro na manutenção manual");
            }
        });

        return Ok(new { mensagem = "Manutenção iniciada em background" });
    }
}