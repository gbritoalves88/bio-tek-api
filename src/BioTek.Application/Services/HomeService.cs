using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class HomeService : IHomeService
{
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;
    private readonly IRepository<Notificacao> _notificacaoRepository; // 🆕
    private readonly IStreakService _streakService;
    private readonly ILogger<HomeService> _logger; // 🆕

    public HomeService(
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Notificacao> notificacaoRepository, // 🆕
        IStreakService streakService,
        ILogger<HomeService> logger) // 🆕
    {
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _notificacaoRepository = notificacaoRepository;
        _streakService = streakService;
        _logger = logger;
    }

    public async Task<object> GetDashboardAsync(string usuarioId)
    {
        var todosLivros = await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId);
        var todasAvaliacoes = await _avaliacaoRepository.GetAllAsync();
        var todosLivrosCatalogo = await _livroRepository.GetAllAsync();

        // 1. Livro atual
        var livroAtual = todosLivros
            .Where(l => l.Status == StatusLeitura.Lendo)
            .OrderByDescending(l => l.UltimaLeitura ?? l.DataAtualizacao ?? l.DataCriacao)
            .FirstOrDefault(l => !l.Pausado);

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
                l.DataCriacao
            })
            .ToList();

        // 3. Recomendados
        var topAvaliados = todasAvaliacoes
            .Where(a => a.IdUsuario != usuarioId)
            .GroupBy(a => a.IdLivro)
            .Where(g => g.Average(a => (int)a.Nota) >= 4)
            .Select(g => new
            {
                LivroId = g.Key,
                Media = Math.Round(g.Average(a => (int)a.Nota), 1),
                Total = g.Count()
            })
            .OrderBy(x => Guid.NewGuid())
            .Take(10)
            .ToList()
            .Select(item =>
            {
                var livro = todosLivrosCatalogo.FirstOrDefault(l => l.Id == item.LivroId);
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
                    item.Media,
                    TotalAvaliacoes = item.Total
                };
            })
            .Where(x => x != null)
            .ToList();

        // 4. Streak
        var streak = await _streakService.GetStreakAsync(usuarioId);

        // 🆕 5. Gerar notificações de incentivo
        await GerarNotificacoesIncentivo(usuarioId, livroAtual, todosLivros, streak);

        return new
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
                livroAtual.MinutoAtual
            },
            listaLeitura,
            topAvaliados,
            streak
        };
    }

    // 🆕 Método para gerar notificações de incentivo
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
                var sequenciaAtual = (int)streakData?.sequenciaAtual; // 🆕 minúscula
                var ultimaAtividade = streakData?.ultimaDataAtividade as DateTime?; // 🆕 minúscula
                
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

    // 🆕 Verificar se já tem notificação do tipo nas últimas 24h
    private async Task<bool> JaTemNotificacao(string usuarioId, string tipo)
    {
        var existentes = await _notificacaoRepository.FindAsync(n =>
            n.IdUsuario == usuarioId &&
            n.Tipo == tipo &&
            n.DataCriacao > DateTime.UtcNow.AddHours(-24)
        );
        return existentes.Any();
    }

    // 🆕 Criar notificação
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