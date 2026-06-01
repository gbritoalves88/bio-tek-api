using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class ComunidadeService : IComunidadeService
{
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IRepository<ComentarioLivro> _comentarioRepository;

    public ComunidadeService(
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Usuario> usuarioRepository,
        IRepository<ComentarioLivro> comentarioRepository) // ⬅️ NOVO
    {
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _usuarioRepository = usuarioRepository;
        _comentarioRepository = comentarioRepository;
    }

     public async Task<List<AtividadeFeed>> GetFeedAsync(string? meuId = null, int limite = 30)
    {
        // ⬅️ VALIDAÇÃO INICIAL
        if (string.IsNullOrEmpty(meuId))
        {
            return new List<AtividadeFeed>();
        }

        var atividades = new List<AtividadeFeed>();

        // Busca dados
        var usuarios = await _usuarioRepository.GetAllAsync();
        var usuarioMap = usuarios.ToDictionary(u => u.Id, u => u);

        var livros = await _livroRepository.GetAllAsync();
        var livroMap = livros.ToDictionary(l => l.Id, l => l);

        var comentarios = await _comentarioRepository.GetAllAsync();
        
        // ===== AVALIAÇÕES =====
        var avaliacoes = await _avaliacaoRepository.GetAllAsync();
        
        var ultimasAvaliacoes = avaliacoes
            .Where(a => !string.IsNullOrEmpty(a.IdUsuario) && a.IdUsuario != meuId)
            .OrderByDescending(a => a.DataAtualizacao ?? a.DataCriacao)
            .Take(limite)
            .ToList();

        foreach (var av in ultimasAvaliacoes)
        {
            // ⬅️ VERIFICA SE O ID É NULO
            if (string.IsNullOrEmpty(av.IdUsuario)) continue;
            
            // ⬅️ USA TRYGETVALUE PARA EVITAR ERRO
            usuarioMap.TryGetValue(av.IdUsuario, out var u);
            livroMap.TryGetValue(av.IdLivro, out var livro);
            
            // Comentários deste livro
            var comentariosDoLivro = comentarios
                .Where(c => c.IdLivro == av.IdLivro)
                .Select(c => new
                {
                    c.Id,
                    c.Comentario,
                    c.DataCriacao,
                    Usuario = usuarioMap.TryGetValue(c.IdUsuario, out var cu) ? new
                    {
                        cu.Id,
                        cu.Nickname
                    } : null
                }).ToList<object>();

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
                Comentarios = comentariosDoLivro,
                Data = av.DataAtualizacao ?? av.DataCriacao
            });
        }

        // ===== LIVROS =====
        var ultimosLivros = livros
            .Where(l => !string.IsNullOrEmpty(l.IdUsuario) && l.IdUsuario != meuId)
            .OrderByDescending(l => l.DataAtualizacao ?? l.DataCriacao)
            .Take(limite);

        foreach (var livro in ultimosLivros)
        {
            if (string.IsNullOrEmpty(livro.IdUsuario)) continue;
            
            usuarioMap.TryGetValue(livro.IdUsuario, out var u);
            
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

        return atividades
            .OrderByDescending(a => a.Data)
            .Take(limite)
            .ToList();
    }
}