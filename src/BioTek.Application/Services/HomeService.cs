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
        // Busca TODOS os livros do usuário (1 requisição)
        var todosLivros = await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId);
        
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

        // 3. Top 10 melhores avaliados da plataforma (EXCLUINDO o usuário logado)
        var todasAvaliacoes = await _avaliacaoRepository.GetAllAsync();
        
        // Filtra avaliações de outros usuários
        var avaliacoesOutros = todasAvaliacoes
            .Where(a => a.IdUsuario != usuarioId)
            .ToList();
        
        var topAvaliadosIds = avaliacoesOutros
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

        // Busca apenas os livros que estão no top
        var todosLivrosCatalogo = await _livroRepository.GetAllAsync();
        
        var topAvaliados = todosLivrosCatalogo
            .Where(l => topAvaliadosIds.Contains(l.Id))
            .Select(l => new
            {
                l.Id,
                l.Titulo,
                l.Autor,
                l.Imagem,
                Media = avaliacoesOutros
                    .Where(a => a.IdLivro == l.Id)
                    .Average(a => (int)a.Nota),
                TotalAvaliacoes = avaliacoesOutros
                    .Count(a => a.IdLivro == l.Id)
            })
            .OrderByDescending(l => l.Media)
            .ThenByDescending(l => l.TotalAvaliacoes)
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
                livroAtual.UltimaLeitura 
            },
            listaLeitura,
            topAvaliados
        };
    }
}