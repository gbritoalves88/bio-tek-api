using System.Text.Json;
using BioTek.Api.Models;
using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace BioTek.Application.Services;

public class LivroService : ILivroService
{
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<Colecao> _colecaoRepository;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IRepository<ComentarioLivro> _comentarioLivroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoLivroRepository;
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IStreakService _streakService;
    private readonly IRepository<Catalogo> _catalogoRepository;

    private readonly IHomeService _homeService;
    public LivroService(
        IRepository<Livro> livroRepository,
        IRepository<Colecao> colecaoRepository,
        HttpClient httpClient,
        IConfiguration configuration,
        IRepository<ComentarioLivro> comentarioLivroRepository,
        IRepository<AvaliacaoLivro> avaliacaoLivroRepository,
        IRepository<Usuario> usuarioRepository,
        IRepository<Catalogo> catalogoRepository,
        IStreakService streakService,
        IHomeService homeService)
    {
        _livroRepository = livroRepository;
        _colecaoRepository = colecaoRepository;
        _httpClient = httpClient;
        _configuration = configuration;
        _comentarioLivroRepository = comentarioLivroRepository;
        _avaliacaoLivroRepository = avaliacaoLivroRepository;
        _usuarioRepository = usuarioRepository;
        _catalogoRepository = catalogoRepository;
        _streakService = streakService;
        _homeService = homeService;
    }

    // ========== BUSCAS ==========

    public async Task<IEnumerable<Livro>> GetPorUsuarioAsync(string usuarioId)
    {
        return await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId);
    }

    public async Task<List<object>> GetMeusLivrosAsync(string usuarioId)
    {
        var livros = await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId);
        var avaliacoes = await _avaliacaoLivroRepository.FindAsync(a => a.IdUsuario == usuarioId);

        return livros.Select(l =>
        {
            var av = avaliacoes.FirstOrDefault(a => a.IdLivro == l.Id);
            return (object)new
            {
                l.Id,
                l.IdUsuario,
                l.IdColecao,
                l.Titulo,
                l.Autor,
                l.Descricao,
                l.CodigoBarras,
                l.Imagem,
                l.Status,
                l.Progresso,
                l.DataCriacao,
                l.DataAtualizacao,
                l.Tipo,
                l.MinutoAtual,
                l.TotalMinutos,
                l.PaginaAtual,
                l.TotalPaginas,
                l.Pausado,
                l.UltimaLeitura,
                l.Generos,
                l.AnoPublicacao,
                l.Editora,
                l.Idioma,
                Avaliacao = av == null ? null : new { av.Id, av.Nota, av.DataCriacao }
            };
        }).ToList();
    }

    public async Task<Livro?> GetPorIdAsync(string id)
    {
        return await _livroRepository.GetByIdAsync(id);
    }

    // ========== CRUD ==========

    public async Task<Livro> CriarAsync(Livro livro)
    {
        var existentes = await _livroRepository.FindAsync(
            l => l.IdUsuario == livro.IdUsuario &&
                l.Titulo.ToLower() == livro.Titulo.ToLower());

        if (existentes.Any())
            throw new InvalidOperationException("Você já tem este livro na sua biblioteca");

        if (!string.IsNullOrEmpty(livro.IdColecao))
        {
            var colecao = await _colecaoRepository.GetByIdAsync(livro.IdColecao);
            if (colecao == null)
                throw new InvalidOperationException("Coleção não encontrada");
        }

        livro.Id = Guid.NewGuid().ToString();
        livro.DataCriacao = DateTime.UtcNow;
        livro.Status = StatusLeitura.QueroLer;

        if (string.IsNullOrEmpty(livro.Tipo))
            livro.Tipo = "livro";

        // Garante listas não nulas
        if (livro.Generos == null)
            livro.Generos = new List<string>();

        await _livroRepository.AddAsync(livro);
        _homeService.LimparCache();
        return livro;
    }

    public async Task<Livro?> AtualizarAsync(string id, Livro livro, int? estrelas = null)
    {
        var existente = await _livroRepository.GetByIdAsync(id);
        if (existente == null) return null;

        if (!string.IsNullOrEmpty(livro.IdColecao))
        {
            var colecao = await _colecaoRepository.GetByIdAsync(livro.IdColecao);
            if (colecao == null)
                throw new InvalidOperationException("Coleção não encontrada");
        }

        livro.Id = id;
        livro.DataCriacao = existente.DataCriacao;
        livro.DataAtualizacao = DateTime.UtcNow;
        livro.IdUsuario = existente.IdUsuario;

        if (string.IsNullOrEmpty(livro.Tipo))
            livro.Tipo = existente.Tipo ?? "livro";

        // Preserva progresso
        livro.Progresso = existente.Progresso;
        livro.PaginaAtual = existente.PaginaAtual;
        livro.TotalPaginas = existente.TotalPaginas;
        livro.MinutoAtual = existente.MinutoAtual;
        livro.TotalMinutos = existente.TotalMinutos;
        livro.UltimaLeitura = existente.UltimaLeitura;
        livro.Pausado = existente.Pausado;

        // Garante listas não nulas
        if (livro.Generos == null)
            livro.Generos = existente.Generos ?? new List<string>();

        await _livroRepository.UpdateAsync(id, livro);

        _homeService.LimparCache();

        if (livro.Status == StatusLeitura.Lido && estrelas.HasValue)
        {
            if (estrelas.Value < 1 || estrelas.Value > 5)
                throw new InvalidOperationException("Nota deve ser entre 1 e 5");

            var nota = (Nota)estrelas.Value;

            var avaliacoesExistentes = await _avaliacaoLivroRepository
                .FindAsync(a => a.IdLivro == id && a.IdUsuario == livro.IdUsuario);
            var avaliacaoExistente = avaliacoesExistentes.FirstOrDefault();

            if (avaliacaoExistente != null)
            {
                avaliacaoExistente.Nota = nota;
                avaliacaoExistente.DataAtualizacao = DateTime.UtcNow;
                await _avaliacaoLivroRepository.UpdateAsync(avaliacaoExistente.Id, avaliacaoExistente);
            }
            else
            {
                var novaAvaliacao = new AvaliacaoLivro
                {
                    Id = Guid.NewGuid().ToString(),
                    IdLivro = id,
                    IdUsuario = livro.IdUsuario,
                    Nota = nota,
                    DataCriacao = DateTime.UtcNow
                };
                await _avaliacaoLivroRepository.AddAsync(novaAvaliacao);
            }
        }

        return livro;
    }

    public async Task<bool> DeletarAsync(string id)
    {
        var existente = await _livroRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _livroRepository.DeleteAsync(id);
        _homeService.LimparCache();
        return true;
    }

    // ========== FEED PÚBLICO (OTIMIZADO) ==========

    public async Task<IEnumerable<object>> GetTodosAsync()
    {
        var livros = await _livroRepository.GetAllAsync();
        
        // Projeção: retorna só campos necessários
        return livros.Select(l => (object)new
        {
            l.Id,
            l.Titulo,
            l.Autor,
            l.Imagem,
            l.Status,
            l.DataCriacao,
            l.Tipo,
            l.Generos,
            l.AnoPublicacao,
            l.Editora,
            l.Idioma
        });
    }

    // ========== BIBLIOTECA COMPLETA (OTIMIZADO) ==========

    public async Task<List<object>> GetBibliotecaCompletaAsync(string usuarioId)
    {
        var livros = (await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId)).ToList();
        
        if (livros.Count == 0) return new List<object>();
        
        var livrosIds = livros.Select(l => l.Id).ToHashSet();
        
        // Busca apenas avaliações do usuário para seus livros
        var avaliacoes = await _avaliacaoLivroRepository.FindAsync(a => 
            a.IdUsuario == usuarioId && livrosIds.Contains(a.IdLivro));
        
        // Busca apenas comentários dos livros do usuário
        var comentarios = await _comentarioLivroRepository.FindAsync(c => 
            livrosIds.Contains(c.IdLivro));
        
        // Busca apenas usuários que comentaram
        var comentadoresIds = comentarios.Select(c => c.IdUsuario).ToHashSet();
        var usuarios = await _usuarioRepository.FindAsync(u => comentadoresIds.Contains(u.Id));
        var usuarioMap = usuarios.ToDictionary(u => u.Id);

        return livros.Select(livro =>
        {
            var avaliacao = avaliacoes.FirstOrDefault(a => a.IdLivro == livro.Id);
            var comentariosDoLivro = comentarios
                .Where(c => c.IdLivro == livro.Id)
                .Select(c => new
                {
                    c.Id,
                    c.Comentario,
                    c.DataCriacao,
                    Usuario = usuarioMap.TryGetValue(c.IdUsuario, out var u) ? new
                    {
                        u.Id,
                        u.Nome,
                        u.Nickname,
                        u.Avatar
                    } : null
                }).ToList();

            return (object)new
            {
                livro.Id,
                livro.Titulo,
                livro.Autor,
                livro.Descricao,
                livro.Imagem,
                livro.CodigoBarras,
                livro.Status,
                livro.Progresso,
                livro.DataCriacao,
                livro.DataAtualizacao,
                livro.Tipo,
                livro.MinutoAtual,
                livro.TotalMinutos,
                livro.PaginaAtual,
                livro.TotalPaginas,
                livro.Pausado,
                livro.UltimaLeitura,
                livro.Generos,
                livro.AnoPublicacao,
                livro.Editora,
                livro.Idioma,
                Avaliacao = avaliacao == null ? null : new { avaliacao.Id, avaliacao.Nota, avaliacao.DataCriacao },
                Comentarios = comentariosDoLivro
            };
        }).ToList();
    }

    // ========== PROGRESSO ==========

    public async Task<Livro?> AtualizarProgressoAsync(string id, string usuarioId, AtualizarProgressoRequest request)
    {
        var existente = await _livroRepository.GetByIdAsync(id);
        if (existente == null || existente.IdUsuario != usuarioId)
            return null;

        // 🆕 Guarda valores anteriores (antes de sobrescrever)
        var pausadoAnterior = existente.Pausado;
        var progressoAnterior = existente.Progresso;
        var paginaAnterior = existente.PaginaAtual ?? 0;
        var minutoAnterior = existente.MinutoAtual ?? 0;
        var primeiraLeitura = !existente.UltimaLeitura.HasValue;

        if (request.PaginaAtual.HasValue)
            existente.PaginaAtual = request.PaginaAtual.Value;

        if (request.TotalPaginas.HasValue)
            existente.TotalPaginas = request.TotalPaginas.Value;

        if (request.Progresso.HasValue)
            existente.Progresso = request.Progresso.Value;

        if (request.Pausado.HasValue)
            existente.Pausado = request.Pausado.Value;

        if (request.MinutoAtual.HasValue)
            existente.MinutoAtual = request.MinutoAtual.Value;

        if (request.TotalMinutos.HasValue)
            existente.TotalMinutos = request.TotalMinutos.Value;

        existente.UltimaLeitura = DateTime.UtcNow;
        existente.DataAtualizacao = DateTime.UtcNow;

        if (request.Pausado == false)
            existente.Status = StatusLeitura.Lendo;

        await _livroRepository.UpdateAsync(id, existente);
        await _streakService.RegistrarAtividadeAsync(usuarioId);
        _homeService.LimparCache();

        // ========== 🆕 REGISTRAR LEITURA DO DIA ==========
        // Só registra se houve mudança real de progresso (página, minuto, pausa ou progresso)
        var teveMudanca = false;
        var paginasDelta = 0;
        var minutosDelta = 0;

        if (request.PaginaAtual.HasValue && request.PaginaAtual.Value != paginaAnterior)
        {
            teveMudanca = true;
            var delta = request.PaginaAtual.Value - paginaAnterior;
            if (delta > 0) paginasDelta = delta;
        }

        if (request.MinutoAtual.HasValue && request.MinutoAtual.Value != minutoAnterior)
        {
            teveMudanca = true;
            var delta = request.MinutoAtual.Value - minutoAnterior;
            if (delta > 0) minutosDelta = delta;
        }

        if (request.Pausado.HasValue && request.Pausado.Value != pausadoAnterior)
            teveMudanca = true;

        if (request.Progresso.HasValue && request.Progresso.Value != progressoAnterior)
            teveMudanca = true;

        if (teveMudanca)
        {
            await _streakService.RegistrarLeituraHojeAsync(
                usuarioId,
                id,
                paginasDelta > 0 ? paginasDelta : (int?)null,
                minutosDelta > 0 ? minutosDelta : (int?)null
            );
        }

        // ========== REGISTRO DE ATIVIDADE (COMENTÁRIO) ==========
        var progressoAtual = existente.Progresso;
        var deveRegistrar = false;
        var mensagem = "";

        if (primeiraLeitura && request.Pausado != true)
        {
            deveRegistrar = true;
            mensagem = "Começou a leitura";
        }
        else if (request.Pausado.HasValue && request.Pausado.Value != pausadoAnterior)
        {
            deveRegistrar = true;
            mensagem = request.Pausado.Value ? "Pausou a leitura" : "Retomou a leitura";
        }
        else if (progressoAtual.HasValue && progressoAnterior != progressoAtual)
        {
            var marco = progressoAtual.Value;
            if (marco == 25 || marco == 50 || marco == 75 || marco == 100)
            {
                deveRegistrar = true;
                mensagem = $"Avançou para {marco}% da leitura";
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Frase))
        {
            deveRegistrar = true;
            mensagem = request.Frase.Trim();
        }

        if (deveRegistrar)
        {
            var atividade = new ComentarioLivro
            {
                Id = Guid.NewGuid().ToString(),
                IdLivro = id,
                IdUsuario = usuarioId,
                Comentario = mensagem,
                Origem = request.Pausado.HasValue && request.Pausado.Value != pausadoAnterior
                    ? (request.Pausado.Value ? "Pausa" : "Retomada")
                    : "Atividade",
                DataCriacao = DateTime.UtcNow,
                Progresso = existente.Progresso,
                PaginaAtual = existente.PaginaAtual,
                TotalPaginas = existente.TotalPaginas,
                MinutoAtual = existente.MinutoAtual,
                TotalMinutos = existente.TotalMinutos
            };
            await _comentarioLivroRepository.AddAsync(atividade);
        }

        return existente;
    }
    // ========== ATIVIDADES (OTIMIZADO) ==========

    public async Task<List<object>> GetAtividadesPorUsuarioAsync(string usuarioId)
    {
        var atividades = (await _comentarioLivroRepository.FindAsync(c => 
            c.IdUsuario == usuarioId && 
            (c.Origem == "Atividade" || c.Origem == "Pausa" || c.Origem == "Retomada"))).ToList();
        
        if (atividades.Count == 0) return new List<object>();
        
        var livrosIds = atividades.Select(a => a.IdLivro).ToHashSet();
        
        // Busca apenas livros das atividades
        var livros = await _livroRepository.FindAsync(l => livrosIds.Contains(l.Id));

        return atividades
            .Select(a => new
            {
                a.Id,
                a.Comentario,
                a.DataCriacao,
                a.Origem,
                a.Progresso,
                a.PaginaAtual,
                a.TotalPaginas,
                a.MinutoAtual,
                a.TotalMinutos,
                Livro = livros.FirstOrDefault(l => l.Id == a.IdLivro) is Livro livro ? new
                {
                    livro.Id,
                    livro.Titulo,
                    livro.Autor,
                    livro.Imagem,
                    livro.Tipo
                } : null
            })
            .OrderByDescending(a => a.DataCriacao)
            .ToList<object>();
    }

    // ========== DETALHES INTERNOS ==========

    public async Task<object?> GetDetalhesInternoAsync(string id)
    {
        // 1. Buscar no Catálogo
        var catalogo = await _catalogoRepository.GetByIdAsync(id);
        if (catalogo != null)
        {
            var livrosMesmoTitulo = await _livroRepository.FindAsync(l => 
                l.Titulo != null && l.Titulo == catalogo.Titulo);

            var avaliacoesCatalogo = await _avaliacaoLivroRepository.FindAsync(a => a.IdLivro == id);

            return new
            {
                AvaliacaoMedia = avaliacoesCatalogo.Any() ? Math.Round(avaliacoesCatalogo.Average(a => (int)a.Nota), 1) : 0,
                TotalAvaliacoes = avaliacoesCatalogo.Count(),
                TotalNaBiblioteca = livrosMesmoTitulo.Count(),
                TotalLendo = livrosMesmoTitulo.Count(l => l.Status == StatusLeitura.Lendo),
                TotalLido = livrosMesmoTitulo.Count(l => l.Status == StatusLeitura.Lido),
                TotalQueroLer = livrosMesmoTitulo.Count(l => l.Status == StatusLeitura.QueroLer)
            };
        }

        // 2. Buscar na biblioteca pessoal
        var livro = await _livroRepository.GetByIdAsync(id);
        if (livro == null) return null;

        var livrosMesmoTituloLocal = await _livroRepository.FindAsync(l => 
            l.Titulo != null && l.Titulo == livro.Titulo);

        var avaliacoes = await _avaliacaoLivroRepository.FindAsync(a => a.IdLivro == id);

        return new
        {
            AvaliacaoMedia = avaliacoes.Any() ? Math.Round(avaliacoes.Average(a => (int)a.Nota), 1) : 0,
            TotalAvaliacoes = avaliacoes.Count(),
            TotalNaBiblioteca = livrosMesmoTituloLocal.Count(),
            TotalLendo = livrosMesmoTituloLocal.Count(l => l.Status == StatusLeitura.Lendo),
            TotalLido = livrosMesmoTituloLocal.Count(l => l.Status == StatusLeitura.Lido),
            TotalQueroLer = livrosMesmoTituloLocal.Count(l => l.Status == StatusLeitura.QueroLer)
        };
    }

    // ========== ISBN ==========

    public async Task<Livro?> BuscarPorIsbnAsync(string isbn)
    {
        var livro = await BuscarOpenLibraryBooks(isbn);
        if (livro != null) return livro;

        livro = await BuscarOpenLibrarySearch(isbn);
        if (livro != null) return livro;

        var apiKey = _configuration["BooksApi:GoogleApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            livro = await BuscarGoogleBooks(isbn, apiKey);
            if (livro != null) return livro;
        }

        return null;
    }

    private async Task<Livro?> BuscarOpenLibraryBooks(string isbn)
    {
        try
        {
            var url = $"https://openlibrary.org/api/books?bibkeys=ISBN:{isbn}&format=json&jscmd=data";
            var response = await _httpClient.GetStringAsync(url);
            var json = JsonDocument.Parse(response);

            var key = $"ISBN:{isbn}";
            if (!json.RootElement.TryGetProperty(key, out var bookElement))
                return null;

            return new Livro
            {
                Titulo = bookElement.GetProperty("title").GetString() ?? "",
                Autor = GetAutor(bookElement),
                Descricao = GetDescricao(bookElement),
                Imagem = GetCapa(bookElement),
                CodigoBarras = isbn,
                Status = StatusLeitura.QueroLer,
                Tipo = "livro"
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<Livro?> BuscarOpenLibrarySearch(string isbn)
    {
        try
        {
            var url = $"https://openlibrary.org/search.json?isbn={isbn}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JsonDocument.Parse(response);

            if (!json.RootElement.TryGetProperty("docs", out var docs) || docs.GetArrayLength() == 0)
                return null;

            var doc = docs[0];

            return new Livro
            {
                Titulo = doc.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                Autor = doc.TryGetProperty("author_name", out var authors) && authors.GetArrayLength() > 0
                    ? authors[0].GetString() ?? "Autor desconhecido"
                    : "Autor desconhecido",
                Descricao = doc.TryGetProperty("first_sentence", out var desc) && desc.GetArrayLength() > 0
                    ? desc[0].GetString()
                    : null,
                Imagem = doc.TryGetProperty("cover_i", out var coverId)
                    ? $"https://covers.openlibrary.org/b/id/{coverId.GetInt32()}-L.jpg"
                    : null,
                CodigoBarras = isbn,
                Status = StatusLeitura.QueroLer,
                Tipo = "livro"
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<Livro?> BuscarGoogleBooks(string isbn, string apiKey)
    {
        try
        {
            var url = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn}&key={apiKey}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JsonDocument.Parse(response);

            if (!json.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
                return null;

            var info = items[0].GetProperty("volumeInfo");

            return new Livro
            {
                Titulo = info.GetProperty("title").GetString() ?? "",
                Autor = info.TryGetProperty("authors", out var a) && a.GetArrayLength() > 0
                    ? string.Join(", ", a.EnumerateArray().Select(x => x.GetString()))
                    : "Autor desconhecido",
                Descricao = info.TryGetProperty("description", out var d)
                    ? (d.GetString()?.Length > 500 ? d.GetString()?[..500] + "..." : d.GetString())
                    : null,
                Imagem = info.TryGetProperty("imageLinks", out var img) && img.TryGetProperty("thumbnail", out var t)
                    ? t.GetString()
                    : null,
                CodigoBarras = isbn,
                Status = StatusLeitura.QueroLer,
                Tipo = "livro"
            };
        }
        catch
        {
            return null;
        }
    }

    private string GetAutor(JsonElement book)
    {
        if (book.TryGetProperty("authors", out var authors) && authors.GetArrayLength() > 0)
            return authors[0].GetProperty("name").GetString() ?? "Autor desconhecido";
        return "Autor desconhecido";
    }

    private string? GetDescricao(JsonElement book)
    {
        if (book.TryGetProperty("notes", out var notes))
            return notes.GetString();
        if (book.TryGetProperty("subtitle", out var subtitle))
            return subtitle.GetString();
        return null;
    }

    private string? GetCapa(JsonElement book)
    {
        if (book.TryGetProperty("cover", out var cover))
        {
            if (cover.TryGetProperty("large", out var large))
                return large.GetString();
            if (cover.TryGetProperty("medium", out var medium))
                return medium.GetString();
        }
        return null;
    }
}