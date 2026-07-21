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

        // 3. Mix: 5 bem avaliados (4+) + 5 mais adicionados
        var idsUsados = new HashSet<string>();
        var topAvaliados = new List<object>();

        // 3a. Bem avaliados (nota 4+)
        var bemAvaliados = todasAvaliacoes
            .Where(a => a.IdUsuario != usuarioId)
            .GroupBy(a => a.IdLivro)
            .Where(g => g.Average(a => (int)a.Nota) >= 4)
            .Select(g => new
            {
                LivroId = g.Key,
                Media = g.Average(a => (int)a.Nota),
                Total = g.Count()
            })
            .OrderBy(x => Guid.NewGuid())
            .Take(5)
            .ToList();

        foreach (var item in bemAvaliados)
        {
            var livro = todosLivrosCatalogo.FirstOrDefault(l => l.Id == item.LivroId);
            if (livro != null && idsUsados.Add(livro.Id))
            {
                topAvaliados.Add(new
                {
                    livro.Id,
                    livro.Titulo,
                    livro.Autor,
                    livro.Imagem,
                    Media = Math.Round(item.Media, 1),
                    TotalAvaliacoes = item.Total
                });
            }
        }

        // 3b. Mais adicionados
        var maisAdicionados = todosLivrosCatalogo
            .Where(l => l.IdUsuario != usuarioId)
            .GroupBy(l => l.Titulo.ToLower())
            .Select(g => new
            {
                Quantidade = g.Count(),
                Primeiro = g.First()
            })
            .OrderByDescending(x => x.Quantidade)
            .ThenBy(x => Guid.NewGuid())
            .Take(5)
            .ToList();

        foreach (var item in maisAdicionados)
        {
            if (idsUsados.Add(item.Primeiro.Id))
            {
                var avs = todasAvaliacoes.Where(a => a.IdLivro == item.Primeiro.Id).ToList();
                topAvaliados.Add(new
                {
                    item.Primeiro.Id,
                    item.Primeiro.Titulo,
                    item.Primeiro.Autor,
                    item.Primeiro.Imagem,
                    Media = avs.Any() ? Math.Round(avs.Average(a => (int)a.Nota), 1) : 0,
                    TotalAvaliacoes = avs.Count
                });
            }
        }

        topAvaliados = topAvaliados.OrderBy(x => Guid.NewGuid()).Take(10).ToList();

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
                livroAtual.UltimaLeitura
            },
            listaLeitura,
            topAvaliados
        };
    }
}