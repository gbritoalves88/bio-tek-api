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

    // 🆕 Cache do feed por usuário
    private static readonly Dictionary<string, (List<AtividadeFeed> Feed, DateTime Time)> _cacheFeed = new();
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromSeconds(60);

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

        // 🆕 Limitar o limite para evitar abuso
        limite = Math.Clamp(limite, 1, 50);

        // 🆕 Cache por usuário
        var cacheKey = $"{meuId}_{limite}";
        
        lock (_cacheLock)
        {
            if (_cacheFeed.TryGetValue(cacheKey, out var cached) && 
                DateTime.UtcNow - cached.Time < CACHE_DURATION)
            {
                return cached.Feed;
            }
        }

        var ultimosDias = DateTime.UtcNow.AddDays(-30);
        var atividades = new List<AtividadeFeed>();

        // ============ 1. BUSCAR DADOS COM FILTRO NO BANCO ============
        var tarefas = new
        {
            Seguindo = _seguidorRepository.FindAsync(s => s.IdSeguidor == meuId),
            AtividadesLeitura = _comentarioRepository.FindAsync(c => 
                c.Origem == "Atividade" && 
                c.IdUsuario != null && 
                c.IdUsuario != meuId &&
                c.DataCriacao >= ultimosDias),
            Avaliacoes = _avaliacaoRepository.FindAsync(a => 
                a.IdUsuario != null && 
                a.IdUsuario != meuId &&
                (a.DataAtualizacao ?? a.DataCriacao) >= ultimosDias),
            Livros = _livroRepository.FindAsync(l => 
                l.IdUsuario != null && 
                l.IdUsuario != meuId &&
                (l.DataAtualizacao ?? l.DataCriacao) >= ultimosDias)
        };

        await Task.WhenAll(tarefas.Seguindo, tarefas.AtividadesLeitura, tarefas.Avaliacoes, tarefas.Livros);

        var seguindo = await tarefas.Seguindo;
        var atividadesLeitura = await tarefas.AtividadesLeitura;
        var avaliacoes = await tarefas.Avaliacoes;
        var livros = await tarefas.Livros;

        // ============ 2. BUSCAR DADOS RELACIONADOS ============
        var idsUsuarios = new HashSet<string>();
        idsUsuarios.UnionWith(atividadesLeitura.Select(c => c.IdUsuario).Where(id => id != null)!);
        idsUsuarios.UnionWith(avaliacoes.Select(a => a.IdUsuario).Where(id => id != null)!);
        idsUsuarios.UnionWith(livros.Select(l => l.IdUsuario).Where(id => id != null)!);

        var idsLivros = new HashSet<string>();
        idsLivros.UnionWith(atividadesLeitura.Select(c => c.IdLivro).Where(id => id != null)!);
        idsLivros.UnionWith(avaliacoes.Select(a => a.IdLivro).Where(id => id != null)!);
        idsLivros.UnionWith(livros.Select(l => l.Id).Where(id => id != null)!);

        var idsAtividades = new HashSet<string>();
        idsAtividades.UnionWith(atividadesLeitura.Select(c => c.Id));
        idsAtividades.UnionWith(avaliacoes.Select(a => a.Id));
        idsAtividades.UnionWith(livros.Select(l => l.Id));

        var tarefasDados = new
        {
            Usuarios = _usuarioRepository.FindAsync(u => idsUsuarios.Contains(u.Id)),
            LivrosRelacionados = _livroRepository.FindAsync(l => idsLivros.Contains(l.Id)),
            ComentariosLivros = _comentarioRepository.FindAsync(c => 
                idsLivros.Contains(c.IdLivro) && 
                c.Origem != "Atividade"),
            Curtidas = _curtidaRepository.FindAsync(c => 
                idsAtividades.Contains(c.IdAtividade))
        };

        await Task.WhenAll(tarefasDados.Usuarios, tarefasDados.LivrosRelacionados, 
            tarefasDados.ComentariosLivros, tarefasDados.Curtidas);

        var usuarios = await tarefasDados.Usuarios;
        var livrosRelacionados = await tarefasDados.LivrosRelacionados;
        var comentariosLivros = await tarefasDados.ComentariosLivros;
        var curtidas = await tarefasDados.Curtidas;

        // ============ 3. CRIAR MAPS PARA ACESSO RÁPIDO ============
        var usuarioMap = usuarios.ToDictionary(u => u.Id, u => u);
        var livroMap = livrosRelacionados.ToDictionary(l => l.Id, l => l);
        var seguindoIds = new HashSet<string>(seguindo.Select(s => s.IdUsuario));
        
        var curtidasPorAtividade = curtidas
            .GroupBy(c => c.IdAtividade)
            .ToDictionary(g => g.Key, g => g.ToList());

        var comentariosPorLivro = comentariosLivros
            .GroupBy(c => c.IdLivro)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ============ 4. PROCESSAR ATIVIDADES DE LEITURA ============
        var atividadesLeituraLimitadas = atividadesLeitura
            .OrderByDescending(c => c.DataCriacao)
            .Take(limite / 3)
            .ToList();

        foreach (var atividade in atividadesLeituraLimitadas)
        {
            if (string.IsNullOrEmpty(atividade.IdUsuario)) continue;

            usuarioMap.TryGetValue(atividade.IdUsuario, out var u);
            livroMap.TryGetValue(atividade.IdLivro, out var livro);
            if (livro == null) continue;

            var curtidasAtividade = curtidasPorAtividade.GetValueOrDefault(atividade.Id, new List<Curtida>());

            atividades.Add(new AtividadeFeed
            {
                Id = atividade.Id,
                Tipo = "livro_progresso",
                Usuario = CriarUsuarioFeed(u, seguindoIds, atividade.IdUsuario),
                Livro = CriarLivroFeed(livro),
                Data = atividade.DataCriacao,
                Curtidas = curtidasAtividade.Count,
                Curtiu = curtidasAtividade.Any(c => c.IdUsuario == meuId),
                Progresso = atividade.Progresso,
                PaginaAtual = atividade.PaginaAtual,
                TotalPaginas = atividade.TotalPaginas,
                MinutoAtual = atividade.MinutoAtual,
                TotalMinutos = atividade.TotalMinutos,
                Frase = atividade.Comentario
            });
        }

        // ============ 5. PROCESSAR AVALIAÇÕES ============
        var avaliacoesLimitadas = avaliacoes
            .OrderByDescending(a => a.DataAtualizacao ?? a.DataCriacao)
            .Take(limite / 3)
            .ToList();

        foreach (var av in avaliacoesLimitadas)
        {
            if (string.IsNullOrEmpty(av.IdUsuario)) continue;

            usuarioMap.TryGetValue(av.IdUsuario, out var u);
            livroMap.TryGetValue(av.IdLivro, out var livro);
            if (livro == null) continue;

            var comentariosDoLivro = comentariosPorLivro
                .GetValueOrDefault(av.IdLivro, new List<ComentarioLivro>())
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

            var curtidasAtividade = curtidasPorAtividade.GetValueOrDefault(av.Id, new List<Curtida>());

            atividades.Add(new AtividadeFeed
            {
                Id = av.Id,
                Tipo = "livro_avaliado",
                Usuario = CriarUsuarioFeed(u, seguindoIds, av.IdUsuario),
                Livro = CriarLivroFeed(livro),
                Avaliacao = new AvaliacaoFeed { Nota = (int)av.Nota },
                Comentarios = comentariosDoLivro,
                Data = av.DataAtualizacao ?? av.DataCriacao,
                Curtidas = curtidasAtividade.Count,
                Curtiu = curtidasAtividade.Any(c => c.IdUsuario == meuId)
            });
        }

        // ============ 6. PROCESSAR LIVROS ============
        var livrosComAtividade = new HashSet<string>(
            atividadesLeitura.Select(c => c.IdLivro)
        );

        var livrosLimitados = livros
            .Where(l => !livrosComAtividade.Contains(l.Id))
            .OrderByDescending(l => l.DataAtualizacao ?? l.DataCriacao)
            .Take(limite / 3)
            .ToList();

        foreach (var livro in livrosLimitados)
        {
            if (string.IsNullOrEmpty(livro.IdUsuario)) continue;

            usuarioMap.TryGetValue(livro.IdUsuario, out var u);

            var curtidasAtividade = curtidasPorAtividade.GetValueOrDefault(livro.Id, new List<Curtida>());

            var atividade = new AtividadeFeed
            {
                Id = livro.Id,
                Tipo = livro.Status == StatusLeitura.Lendo ? "livro_lendo" :
                    livro.Status == StatusLeitura.Lido ? "livro_lido" : "livro_novo",
                Usuario = CriarUsuarioFeed(u, seguindoIds, livro.IdUsuario),
                Livro = CriarLivroFeed(livro),
                Data = livro.DataAtualizacao ?? livro.DataCriacao,
                Curtidas = curtidasAtividade.Count,
                Curtiu = curtidasAtividade.Any(c => c.IdUsuario == meuId)
            };

            if (livro.Status != StatusLeitura.Lendo)
            {
                atividade.Progresso = livro.Progresso;
                atividade.PaginaAtual = livro.PaginaAtual;
                atividade.TotalPaginas = livro.TotalPaginas;
            }

            atividades.Add(atividade);
        }

        var resultado = atividades
            .OrderByDescending(a => a.Data)
            .Take(limite)
            .ToList();

        // 🆕 Salvar no cache
        lock (_cacheLock)
        {
            _cacheFeed[cacheKey] = (resultado, DateTime.UtcNow);
        }

        return resultado;
    }

    public async Task CurtirAsync(string idAtividade, string idUsuario, string tipoAtividade, 
        string? idUsuarioDono = null, string? nomeCurtidor = null, string? tituloLivro = null)
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

            // 🆕 Limpar cache do feed (curtida mudou)
            LimparCacheFeed();

            if (!string.IsNullOrEmpty(idUsuarioDono) && idUsuarioDono != idUsuario)
            {
                await _notificacaoService.CriarNotificacaoCurtida(
                    idUsuarioDono, idAtividade, nomeCurtidor ?? "Usuário", 
                    tipoAtividade, tituloLivro ?? "Livro");
            }
        }
    }

    public async Task DescurtirAsync(string idAtividade, string idUsuario)
    {
        var existentes = await _curtidaRepository.FindAsync(
            c => c.IdAtividade == idAtividade && c.IdUsuario == idUsuario);

        foreach (var c in existentes)
            await _curtidaRepository.DeleteAsync(c.Id);

        // 🆕 Limpar cache do feed (curtida mudou)
        LimparCacheFeed();
    }

    // ============ MÉTODOS AUXILIARES ============

    private static UsuarioFeed? CriarUsuarioFeed(Usuario? u, HashSet<string> seguindoIds, string idUsuario)
    {
        if (u == null) return null;
        
        return new UsuarioFeed
        {
            Id = u.Id,
            Nome = u.Nome,
            Nickname = u.Nickname,
            Avatar = u.Avatar,
            Segue = seguindoIds.Contains(idUsuario)
        };
    }

    private static LivroFeed? CriarLivroFeed(Livro? livro)
    {
        if (livro == null) return null;
        
        return new LivroFeed
        {
            Id = livro.Id,
            Titulo = livro.Titulo,
            Autor = livro.Autor,
            Imagem = livro.Imagem
        };
    }

    // 🆕 Limpar cache do feed
    private void LimparCacheFeed()
    {
        lock (_cacheLock)
        {
            _cacheFeed.Clear();
        }
    }
}