using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class HomeService : IHomeService
{
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;

    public HomeService(
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository)
    {
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
    }

  public async Task<object> GetDashboardAsync(string usuarioId)
{
    var todosLivros = await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId);
    var todasAvaliacoes = await _avaliacaoRepository.GetAllAsync();
    var todosLivrosCatalogo = await _livroRepository.GetAllAsync();

    // 1. Livro atual (status = Lendo - 2)
    var livroAtual = todosLivros
        .FirstOrDefault(l => l.Status == StatusLeitura.Lendo);

    // 2. Lista de leitura (status = Quero ler - 1)
    var listaLeitura = todosLivros
        .Where(l => l.Status == StatusLeitura.QueroLer)
        .OrderByDescending(l => l.DataCriacao)
        .Take(10)
        .Select(l => new
        {
            l.Id,
            l.Titulo,
            l.Autor,
            l.Imagem,
            l.Status,
            l.DataCriacao
        })
        .ToList();

    // 3. Recomendados: apenas bem avaliados (4+) por outros usuários
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
                livro.Imagem,
                item.Media,
                TotalAvaliacoes = item.Total
            };
        })
        .Where(x => x != null)
        .ToList();

    return new
    {
        livroAtual = livroAtual == null ? null : new
        {
            livroAtual.Id,
            livroAtual.Titulo,
            livroAtual.Autor,
            livroAtual.Imagem,
            livroAtual.Status,
            livroAtual.Progresso,
            livroAtual.PaginaAtual,
            livroAtual.TotalPaginas,
            livroAtual.Pausado,
            livroAtual.UltimaLeitura,
            livroAtual.Tipo, livroAtual.MinutoAtual, livroAtual.TotalMinutos
        },
        listaLeitura,
        topAvaliados
    };
}
}