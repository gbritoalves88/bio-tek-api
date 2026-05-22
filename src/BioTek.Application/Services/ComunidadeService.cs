using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class ComunidadeService : IComunidadeService
{
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;
    private readonly IRepository<Usuario> _usuarioRepository;

    public ComunidadeService(
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Usuario> usuarioRepository)
    {
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _usuarioRepository = usuarioRepository;
    }

    public async Task<List<AtividadeFeed>> GetFeedAsync(string? meuId = null, int limite = 30)
    {
        var atividades = new List<AtividadeFeed>();

        var usuarios = await _usuarioRepository.GetAllAsync();
        var usuarioMap = usuarios.ToDictionary(u => u.Id);

        var livros = await _livroRepository.GetAllAsync();
        var ultimosLivros = livros
            .Where(l => string.IsNullOrEmpty(meuId) || l.IdUsuario != meuId)
            .OrderByDescending(l => l.DataAtualizacao ?? l.DataCriacao)
            .Take(limite);

        foreach (var livro in ultimosLivros)
        {
            var u = usuarioMap.GetValueOrDefault(livro.IdUsuario);
            atividades.Add(new AtividadeFeed
            {
                Id = livro.Id,
                Tipo = livro.Status == StatusLeitura.Lendo ? "livro_lendo" :
                       livro.Status == StatusLeitura.Lido ? "livro_lido" : "livro_novo",
                Usuario = u == null ? null : new UsuarioFeed
                {
                    Id = u.Id,
                    Nome = u.Nome,
                    Nickname = u.Nickname,
                    Avatar = u.Avatar
                },
                Livro = new LivroFeed
                {
                    Id = livro.Id,
                    Titulo = livro.Titulo,
                    Autor = livro.Autor,
                    Imagem = livro.Imagem
                },
                Data = livro.DataAtualizacao ?? livro.DataCriacao
            });
        }

        var avaliacoes = await _avaliacaoRepository.GetAllAsync();
        var ultimasAvaliacoes = avaliacoes
            .Where(a => string.IsNullOrEmpty(meuId) || a.IdUsuario != meuId)
            .OrderByDescending(a => a.DataAtualizacao ?? a.DataCriacao)
            .Take(limite);

        foreach (var av in ultimasAvaliacoes)
        {
            var u = usuarioMap.GetValueOrDefault(av.IdUsuario);
            var livro = livros.FirstOrDefault(l => l.Id == av.IdLivro);
            atividades.Add(new AtividadeFeed
            {
                Id = av.Id,
                Tipo = "livro_avaliado",
                Usuario = u == null ? null : new UsuarioFeed
                {
                    Id = u.Id,
                    Nome = u.Nome,
                    Nickname = u.Nickname,
                    Avatar = u.Avatar
                },
                Livro = livro == null ? null : new LivroFeed
                {
                    Id = livro.Id,
                    Titulo = livro.Titulo,
                    Autor = livro.Autor,
                    Imagem = livro.Imagem
                },
                Avaliacao = new AvaliacaoFeed
                {
                    Nota = (int)av.Nota
                },
                Data = av.DataAtualizacao ?? av.DataCriacao
            });
        }

        return atividades
            .OrderByDescending(a => a.Data)
            .Take(limite)
            .ToList();
    }
}