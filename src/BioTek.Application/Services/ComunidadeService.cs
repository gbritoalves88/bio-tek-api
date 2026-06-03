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
    private readonly IRepository<Seguidor> _seguidorRepository;

    public ComunidadeService(
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Usuario> usuarioRepository,
        IRepository<ComentarioLivro> comentarioRepository,
        IRepository<Seguidor> seguidorRepository) // ⬅️ NOVO
    {
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _usuarioRepository = usuarioRepository;
        _comentarioRepository = comentarioRepository;
        _seguidorRepository = seguidorRepository;
    }

    public async Task<List<AtividadeFeed>> GetFeedAsync(string? meuId = null, int limite = 30)
    {
        if (string.IsNullOrEmpty(meuId))
        {
            return new List<AtividadeFeed>();
        }

        var atividades = new List<AtividadeFeed>();

        // ⬇️ 1 REQUISIÇÃO: Buscar tudo de uma vez
        var usuarios = await _usuarioRepository.GetAllAsync();
        var livros = await _livroRepository.GetAllAsync();
        var avaliacoes = await _avaliacaoRepository.GetAllAsync();
        var comentarios = await _comentarioRepository.GetAllAsync();
        var seguindo = await _seguidorRepository.FindAsync(s => s.IdSeguidor == meuId);

        // ⬇️ Cria Dicionários para acesso rápido
        var usuarioMap = usuarios.ToDictionary(u => u.Id, u => u);
        var livroMap = livros.ToDictionary(l => l.Id, l => l);
        var seguindoIds = new HashSet<string>(seguindo.Select(s => s.IdUsuario));

        // ⬇️ Processa Avaliações
        var ultimasAvaliacoes = avaliacoes
            .Where(a => !string.IsNullOrEmpty(a.IdUsuario) && a.IdUsuario != meuId)
            .OrderByDescending(a => a.DataAtualizacao ?? a.DataCriacao)
            .Take(limite)
            .ToList();

        foreach (var av in ultimasAvaliacoes)
        {
            if (string.IsNullOrEmpty(av.IdUsuario)) continue;
            
            usuarioMap.TryGetValue(av.IdUsuario, out var u);
            livroMap.TryGetValue(av.IdLivro, out var livro);
            
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
                }).ToList();

            atividades.Add(new AtividadeFeed
            {
                Id = av.Id,
                Tipo = "livro_avaliado",
                Usuario = u == null ? null : new UsuarioFeed
                {
                    Id = u.Id,
                    Nome = u.Nome,
                    Nickname = u.Nickname,
                    Avatar = u.Avatar,
                    Segue = seguindoIds.Contains(av.IdUsuario)
                },
                Livro = livro == null ? null : new LivroFeed
                {
                    Id = livro.Id,
                    Titulo = livro.Titulo,
                    Autor = livro.Autor,
                    Imagem = livro.Imagem
                },
                Avaliacao = new AvaliacaoFeed { Nota = (int)av.Nota },
                Comentarios = comentariosDoLivro,
                Data = av.DataAtualizacao ?? av.DataCriacao
            });
        }

        // ⬇️ Processa Livros (novo, lendo, lido)
        var ultimosLivros = livros
            .Where(l => !string.IsNullOrEmpty(l.IdUsuario) && l.IdUsuario != meuId)
            .OrderByDescending(l => l.DataAtualizacao ?? l.DataCriacao)
            .Take(limite)
            .ToList();

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
                    Avatar = u.Avatar,
                    Segue = seguindoIds.Contains(livro.IdUsuario)
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