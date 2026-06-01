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
        // 1. Lista de leitura (status = Quero ler - 1)
        var todosLivros = await _livroRepository.GetAllAsync();
        
        var listaLeitura = todosLivros
            .Where(l => l.IdUsuario == usuarioId && l.Status == StatusLeitura.QueroLer)
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

        // 2. Top 10 melhores avaliados da plataforma (todos os usuários)
        var todasAvaliacoes = await _avaliacaoRepository.GetAllAsync();
        
        var topAvaliadosIds = todasAvaliacoes
            .GroupBy(a => a.IdLivro)
            .Select(g => new
            {
                LivroId = g.Key,
                Media = g.Average(a => (int)a.Nota),
                Total = g.Count()
            })
            .OrderByDescending(x => x.Media)
            .ThenByDescending(x => x.Total)
            .Take(10)
            .Select(x => x.LivroId)
            .ToList();

        var livrosTop = todosLivros
            .Where(l => topAvaliadosIds.Contains(l.Id))
            .Select(l => new
            {
                l.Id,
                l.Titulo,
                l.Autor,
                l.Imagem,
                Media = todasAvaliacoes
                    .Where(a => a.IdLivro == l.Id)
                    .Average(a => (int)a.Nota),
                TotalAvaliacoes = todasAvaliacoes
                    .Count(a => a.IdLivro == l.Id)
            })
            .OrderByDescending(l => l.Media)
            .ThenByDescending(l => l.TotalAvaliacoes)
            .ToList();

        return new
        {
            listaLeitura,
            topAvaliados = livrosTop
        };
    }
}