using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class HomeService : IHomeService
{
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;
    private readonly IRepository<Notificacao> _notificacaoRepository;
    private readonly IRepository<Usuario> _usuarioRepository; // 🆕
    private readonly IStreakService _streakService;
    private readonly ILogger<HomeService> _logger;

    // 🆕 Cache do dashboard
    private static object? _cacheDashboard;
    private static string? _cacheUsuarioId;
    private static DateTime _cacheTime = DateTime.MinValue;
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromSeconds(10);

    public HomeService(
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Notificacao> notificacaoRepository,
        IRepository<Usuario> usuarioRepository, // 🆕
        IStreakService streakService,
        ILogger<HomeService> logger)
    {
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _notificacaoRepository = notificacaoRepository;
        _usuarioRepository = usuarioRepository;
        _streakService = streakService;
        _logger = logger;
    }

    public async Task<object> GetDashboardAsync(string usuarioId)
    {
        // 🆕 Verifica se usuário está ativo
        var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario == null || !usuario.Ativo)
            throw new UnauthorizedAccessException("Usuário inativo ou não encontrado");

        // 🆕 Verifica cache
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

        // 1. Livros do usuário (filtro no banco)
        var todosLivros = (await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId)).ToList();

        // 2. Avaliações com nota >= 4 (evita GetAllAsync)
        var avaliacoesBoas = (await _avaliacaoRepository.FindAsync(a => (int)a.Nota >= 4)).ToList();

        /// 1. Livro atual — prioridade: Lendo não pausado, senão Lendo pausado
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
                l.Editora,
                l.Idioma
            })
            .ToList();

        // 3. Recomendados (OTIMIZADO - evita GetAllAsync)
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
            .Take(50) // 🆕 Limita ANTES de embaralhar
            .OrderBy(x => Guid.NewGuid())
            .Take(10)
            .ToList();

        // Busca só os livros recomendados (não todos)
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
                    item.Media,
                    TotalAvaliacoes = item.Total
                };
            })
            .Where(x => x != null)
            .ToList();

        // 🆕 Outros pausados (excluindo o livroAtual)
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
                l.Editora,
                l.Idioma
            })
            .ToList();

        // 4. Streak
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
                livroAtual.Idioma
            },
            outrosPausados,
            listaLeitura,
            topAvaliados,
            streak
        };

        // 🆕 Salva no cache
        lock (_cacheLock)
        {
            _cacheDashboard = resultado;
            _cacheUsuarioId = usuarioId;
            _cacheTime = DateTime.UtcNow;
        }

        // 🆕 Só gera notificações se NÃO estava em cache
        await GerarNotificacoesIncentivo(usuarioId, livroAtual, todosLivros, streak);

        return resultado;
    }

    // 🆕 Limpa cache (chamado quando dados mudam)
    public void LimparCache()
    {
        lock (_cacheLock)
        {
            _cacheDashboard = null;
            _cacheUsuarioId = null;
            _cacheTime = DateTime.MinValue;
        }
    }

    // Método para gerar notificações de incentivo
    private async Task GerarNotificacoesIncentivo(
        string usuarioId,
        Livro? livroAtual,
        IEnumerable<Livro> todosLivros,
        object? streak)
    {
        try
        {
            var livrosList = todosLivros.ToList();
            var hoje = DateTime.UtcNow.Date;

            // 1. Notificação de streak
            if (streak != null)
            {
                var streakData = streak as dynamic;
                var sequenciaAtual = (int)streakData?.sequenciaAtual;
                var ultimaAtividade = streakData?.ultimaDataAtividade as DateTime?;
                
                if (sequenciaAtual > 0 && ultimaAtividade?.Date != hoje)
                {
                    if (!await JaTemNotificacao(usuarioId, "streak"))
                    {
                        await CriarNotificacao(
                            usuarioId,
                            "streak",
                            $"🔥 Sua sequência está em {sequenciaAtual} dias! Continue lendo hoje!"
                        );
                    }
                }
            }

            // 2. Notificação de leitura parada
            if (livroAtual != null && livroAtual.UltimaLeitura.HasValue)
            {
                var diasParado = (DateTime.UtcNow - livroAtual.UltimaLeitura.Value).Days;
                if (diasParado >= 3)
                {
                    if (!await JaTemNotificacao(usuarioId, "leitura"))
                    {
                        await CriarNotificacao(
                            usuarioId,
                            "leitura",
                            $"📚 Você está há {diasParado} dias sem ler '{livroAtual.Titulo}'. Continue de onde parou!"
                        );
                    }
                }
            }

            // 3. Notificação de incentivo inicial
            if (!livrosList.Any())
            {
                if (!await JaTemNotificacao(usuarioId, "incentivo"))
                {
                    await CriarNotificacao(
                        usuarioId,
                        "incentivo",
                        "📕 Adicione seu primeiro livro e comece sua jornada literária!"
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar notificações de incentivo");
        }
    }

    private async Task<bool> JaTemNotificacao(string usuarioId, string tipo)
    {
        var existentes = await _notificacaoRepository.FindAsync(n =>
            n.IdUsuario == usuarioId &&
            n.Tipo == tipo &&
            n.DataCriacao > DateTime.UtcNow.AddHours(-24)
        );
        return existentes.Any();
    }

    private async Task CriarNotificacao(string usuarioId, string tipo, string mensagem)
    {
        var notificacao = new Notificacao
        {
            Id = Guid.NewGuid().ToString(),
            IdUsuario = usuarioId,
            Tipo = tipo,
            Mensagem = mensagem,
            DataCriacao = DateTime.UtcNow,
            Lida = false
        };
        await _notificacaoRepository.AddAsync(notificacao);
    }
}