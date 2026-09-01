using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class HomeService : IHomeService
{
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;
    private readonly IStreakService _streakService; // 🆕

    public HomeService(
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IStreakService streakService) // 🆕
    {
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _streakService = streakService;
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
                l.Descricao,      // 🆕
                l.CodigoBarras,
                l.Imagem,
                l.Tipo,           // 🆕
                l.TotalPaginas,   // 🆕
                l.TotalMinutos,   // 🆕
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
                    livro.Descricao,      // 🆕
                    livro.CodigoBarras,
                    livro.Imagem,
                    livro.Tipo,           // 🆕
                    livro.TotalPaginas,   // 🆕
                    livro.TotalMinutos,   // 🆕
                    item.Media,
                    TotalAvaliacoes = item.Total
                };
            })
            .Where(x => x != null)
            .ToList();

        // 4. Streak
        var streak = await _streakService.GetStreakAsync(usuarioId);

        return new
        {
            livroAtual = livroAtual == null ? null : new
            {
                livroAtual.Id,
                livroAtual.Titulo,
                livroAtual.Autor,
                livroAtual.Descricao,      // 🆕
                livroAtual.CodigoBarras,
                livroAtual.Imagem,
                livroAtual.Tipo,           // 🆕
                livroAtual.TotalPaginas,   // 🆕
                livroAtual.TotalMinutos,   // 🆕
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
}