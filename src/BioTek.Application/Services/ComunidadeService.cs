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
    private readonly IRepository<Curtida> _curtidaRepository;
    private readonly INotificacaoService _notificacaoService;

    public ComunidadeService(
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Usuario> usuarioRepository,
        IRepository<ComentarioLivro> comentarioRepository,
        IRepository<Seguidor> seguidorRepository,
        IRepository<Curtida> curtidaRepository,
        INotificacaoService notificacaoService)
    {
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _usuarioRepository = usuarioRepository;
        _comentarioRepository = comentarioRepository;
        _seguidorRepository = seguidorRepository;
        _curtidaRepository = curtidaRepository;
        _notificacaoService = notificacaoService;
    }

    public async Task<List<AtividadeFeed>> GetFeedAsync(string? meuId = null, int limite = 30)
    {
        if (string.IsNullOrEmpty(meuId))
            return new List<AtividadeFeed>();

        var atividades = new List<AtividadeFeed>();

        var usuarios = await _usuarioRepository.GetAllAsync();
        var livros = await _livroRepository.GetAllAsync();
        var avaliacoes = await _avaliacaoRepository.GetAllAsync();
        var comentarios = await _comentarioRepository.GetAllAsync();
        var seguindo = await _seguidorRepository.FindAsync(s => s.IdSeguidor == meuId);
        var curtidas = await _curtidaRepository.GetAllAsync();

        var usuarioMap = usuarios.ToDictionary(u => u.Id, u => u);
        var livroMap = livros.ToDictionary(l => l.Id, l => l);
        var seguindoIds = new HashSet<string>(seguindo.Select(s => s.IdUsuario));

        // ============ 1. PROCESSAR ATIVIDADES DE LEITURA (PROGRESSO) ============
        var atividadesLeitura = comentarios
            .Where(c => c.Origem == "Atividade" && !string.IsNullOrEmpty(c.IdUsuario) && c.IdUsuario != meuId)
            .OrderByDescending(c => c.DataCriacao)
            .Take(limite / 3)
            .ToList();

        foreach (var atividade in atividadesLeitura)
        {
            if (string.IsNullOrEmpty(atividade.IdUsuario)) continue;

            usuarioMap.TryGetValue(atividade.IdUsuario, out var u);
            livroMap.TryGetValue(atividade.IdLivro, out var livro);
            if (livro == null) continue;

            atividades.Add(new AtividadeFeed
            {
                Id = atividade.Id, // ✅ ID da ATIVIDADE (não do livro!)
                Tipo = "livro_progresso",
                Usuario = u == null ? null : new UsuarioFeed
                {
                    Id = u.Id,
                    Nome = u.Nome,
                    Nickname = u.Nickname,
                    Avatar = u.Avatar,
                    Segue = seguindoIds.Contains(atividade.IdUsuario)
                },
                Livro = new LivroFeed
                {
                    Id = livro.Id,
                    Titulo = livro.Titulo,
                    Autor = livro.Autor,
                    Imagem = livro.Imagem
                },
                Data = atividade.DataCriacao,
                Curtidas = curtidas.Count(c => c.IdAtividade == atividade.Id),
                Curtiu = curtidas.Any(c => c.IdAtividade == atividade.Id && c.IdUsuario == meuId),
                Progresso = atividade.Progresso,
                PaginaAtual = atividade.PaginaAtual,
                TotalPaginas = atividade.TotalPaginas,
                MinutoAtual = atividade.MinutoAtual,
                TotalMinutos = atividade.TotalMinutos,
                Frase = atividade.Comentario
            });
        }

        // ============ 2. PROCESSAR AVALIAÇÕES ============
        var ultimasAvaliacoes = avaliacoes
            .Where(a => !string.IsNullOrEmpty(a.IdUsuario) && a.IdUsuario != meuId)
            .OrderByDescending(a => a.DataAtualizacao ?? a.DataCriacao)
            .Take(limite / 3)
            .ToList();

        foreach (var av in ultimasAvaliacoes)
        {
            if (string.IsNullOrEmpty(av.IdUsuario)) continue;

            usuarioMap.TryGetValue(av.IdUsuario, out var u);
            livroMap.TryGetValue(av.IdLivro, out var livro);
            if (livro == null) continue;

            var comentariosDoLivro = comentarios
                .Where(c => c.IdLivro == av.IdLivro && c.Origem != "Atividade")
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
                Livro = new LivroFeed
                {
                    Id = livro.Id,
                    Titulo = livro.Titulo,
                    Autor = livro.Autor,
                    Imagem = livro.Imagem
                },
                Avaliacao = new AvaliacaoFeed { Nota = (int)av.Nota },
                Comentarios = comentariosDoLivro,
                Data = av.DataAtualizacao ?? av.DataCriacao,
                Curtidas = curtidas.Count(c => c.IdAtividade == av.Id),
                Curtiu = curtidas.Any(c => c.IdAtividade == av.Id && c.IdUsuario == meuId)
            });
        }

        // ============ 3. PROCESSAR LIVROS (novo, lendo, lido) ============
        var ultimosLivros = livros
            .Where(l => !string.IsNullOrEmpty(l.IdUsuario) && l.IdUsuario != meuId)
            .OrderByDescending(l => l.DataAtualizacao ?? l.DataCriacao)
            .Take(limite / 3)
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
                Data = livro.DataAtualizacao ?? livro.DataCriacao,
                Curtidas = curtidas.Count(c => c.IdAtividade == livro.Id),
                Curtiu = curtidas.Any(c => c.IdAtividade == livro.Id && c.IdUsuario == meuId),
                Progresso = livro.Progresso,
                PaginaAtual = livro.PaginaAtual,
                TotalPaginas = livro.TotalPaginas
            });
        }

        return atividades
            .OrderByDescending(a => a.Data)
            .Take(limite)
            .ToList();
    }

    public async Task CurtirAsync(string idAtividade, string idUsuario, string tipoAtividade, string? idUsuarioDono = null, string? nomeCurtidor = null, string? tituloLivro = null)
    {
        var existente = await _curtidaRepository.FindAsync(
            c => c.IdAtividade == idAtividade && c.IdUsuario == idUsuario);

        if (!existente.Any())
        {
            var curtida = new Curtida
            {
                Id = Guid.NewGuid().ToString(),
                IdAtividade = idAtividade,
                IdUsuario = idUsuario,
                TipoAtividade = tipoAtividade,
                DataCriacao = DateTime.UtcNow
            };
            await _curtidaRepository.AddAsync(curtida);

            if (!string.IsNullOrEmpty(idUsuarioDono) && idUsuarioDono != idUsuario)
                await _notificacaoService.CriarNotificacaoCurtida(idUsuarioDono, idAtividade, nomeCurtidor!, tipoAtividade, tituloLivro ?? "Livro");
        }
    }

    public async Task DescurtirAsync(string idAtividade, string idUsuario)
    {
        var existentes = await _curtidaRepository.FindAsync(
            c => c.IdAtividade == idAtividade && c.IdUsuario == idUsuario);

        foreach (var c in existentes)
            await _curtidaRepository.DeleteAsync(c.Id);
    }
}