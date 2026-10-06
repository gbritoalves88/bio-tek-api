using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class HomeService : IHomeService
{
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IStreakService _streakService;
    private readonly ILogger<HomeService> _logger;

    // Cache do dashboard
    private static object? _cacheDashboard;
    private static string? _cacheUsuarioId;
    private static DateTime _cacheTime = DateTime.MinValue;
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromSeconds(10);

    public HomeService(
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Usuario> usuarioRepository,
        IStreakService streakService,
        ILogger<HomeService> logger)
    {
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _usuarioRepository = usuarioRepository;
        _streakService = streakService;
        _logger = logger;
    }

    public async Task<object> GetDashboardAsync(string usuarioId)
    {
        // Verifica se usuário está ativo
        var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario == null || !usuario.Ativo)
            throw new UnauthorizedAccessException("Usuário inativo ou não encontrado");

        // Verifica cache
        var cacheHit = false;
        lock (_cacheLock)
        {
            if (_cacheDashboard != null &&
                _cacheUsuarioId == usuarioId &&
                DateTime.UtcNow - _cacheTime < CACHE_DURATION)
            {
                cacheHit = true;
            }
        }

        if (cacheHit)
        {
            return _cacheDashboard!;
        }

        // ========== BUSCA DE DADOS OTIMIZADA ==========

        var todosLivros = (await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId)).ToList();
        var avaliacoesBoas = (await _avaliacaoRepository.FindAsync(a => (int)a.Nota >= 4)).ToList();

        // 1. Livro atual — prioridade: Lendo não pausado, senão Lendo pausado
        var livroAtual = todosLivros
            .Where(l => l.Status == StatusLeitura.Lendo && !l.Pausado)
            .OrderByDescending(l => l.UltimaLeitura ?? l.DataAtualizacao ?? l.DataCriacao)
            .FirstOrDefault();

        if (livroAtual == null)
        {
            livroAtual = todosLivros
                .Where(l => l.Status == StatusLeitura.Lendo && l.Pausado)
                .OrderByDescending(l => l.UltimaLeitura ?? l.DataAtualizacao ?? l.DataCriacao)
                .FirstOrDefault();
        }

        // 2. Lista de leitura
        var listaLeitura = todosLivros
            .Where(l => l.Status == StatusLeitura.QueroLer)
            .OrderByDescending(l => l.DataCriacao)
            .Take(10)
            .Select(l => new
            {
                l.Id,
                l.Titulo,
                l.Autor,
                l.Descricao,
                l.CodigoBarras,
                l.Imagem,
                l.Tipo,
                l.TotalPaginas,
                l.TotalMinutos,
                l.Status,
                l.DataCriacao,
                l.Generos,
                l.AnoPublicacao,
                l.ArquivoLeitura,
                l.Editora,
                l.Idioma
            })
            .ToList();

        // 3. Recomendados
        var recomendadosIds = avaliacoesBoas
            .Where(a => a.IdUsuario != usuarioId)
            .GroupBy(a => a.IdLivro)
            .Where(g => g.Average(a => (int)a.Nota) >= 4)
            .Select(g => new
            {
                LivroId = g.Key,
                Media = Math.Round(g.Average(a => (int)a.Nota), 1),
                Total = g.Count()
            })
            .Take(50)
            .OrderBy(x => Guid.NewGuid())
            .Take(10)
            .ToList();

        var recomendadosIdsSet = recomendadosIds.Select(x => x.LivroId).ToHashSet();
        var livrosRecomendados = (await _livroRepository.FindAsync(l => recomendadosIdsSet.Contains(l.Id))).ToList();

        var topAvaliados = recomendadosIds
            .Select(item =>
            {
                var livro = livrosRecomendados.FirstOrDefault(l => l.Id == item.LivroId);
                if (livro == null) return null;
                return new
                {
                    livro.Id,
                    livro.Titulo,
                    livro.Autor,
                    livro.Descricao,
                    livro.CodigoBarras,
                    livro.Imagem,
                    livro.Tipo,
                    livro.TotalPaginas,
                    livro.TotalMinutos,
                    livro.Generos,
                    livro.AnoPublicacao,
                    livro.Editora,
                    livro.Idioma,
                    livro.ArquivoLeitura,
                    item.Media,
                    TotalAvaliacoes = item.Total
                };
            })
            .Where(x => x != null)
            .ToList();

        // 4. Outros pausados
        var outrosPausados = todosLivros
            .Where(l => l.Status == StatusLeitura.Lendo && l.Pausado && l.Id != livroAtual?.Id)
            .OrderByDescending(l => l.UltimaLeitura ?? l.DataAtualizacao ?? l.DataCriacao)
            .Take(10)
            .Select(l => new
            {
                l.Id,
                l.Titulo,
                l.Autor,
                l.Imagem,
                l.Tipo,
                l.Progresso,
                l.PaginaAtual,
                l.TotalPaginas,
                l.MinutoAtual,
                l.TotalMinutos,
                l.UltimaLeitura,
                l.Generos,
                l.AnoPublicacao,
                l.ArquivoLeitura,
                l.Editora,
                l.Idioma
            })
            .ToList();

        // 5. Streak
        var streak = await _streakService.GetStreakAsync(usuarioId);

        var resultado = new
        {
            livroAtual = livroAtual == null ? null : new
            {
                livroAtual.Id,
                livroAtual.Titulo,
                livroAtual.Autor,
                livroAtual.Descricao,
                livroAtual.CodigoBarras,
                livroAtual.Imagem,
                livroAtual.Tipo,
                livroAtual.TotalPaginas,
                livroAtual.TotalMinutos,
                livroAtual.Status,
                livroAtual.Progresso,
                livroAtual.PaginaAtual,
                livroAtual.Pausado,
                livroAtual.UltimaLeitura,
                livroAtual.MinutoAtual,
                livroAtual.Generos,
                livroAtual.AnoPublicacao,
                livroAtual.Editora,
                livroAtual.ArquivoLeitura,
                livroAtual.Idioma
            },
            outrosPausados,
            listaLeitura,
            topAvaliados,
            streak
        };

        // Salva no cache
        lock (_cacheLock)
        {
            _cacheDashboard = resultado;
            _cacheUsuarioId = usuarioId;
            _cacheTime = DateTime.UtcNow;
        }

        return resultado;
    }

    public void LimparCache()
    {
        lock (_cacheLock)
        {
            _cacheDashboard = null;
            _cacheUsuarioId = null;
            _cacheTime = DateTime.MinValue;
        }
    }
}