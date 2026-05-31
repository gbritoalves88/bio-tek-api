using System.Text.Json;
using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.Configuration;  // ⬅️ ESTA LINHA
using Microsoft.Extensions.Logging; // Opcional, se precisar
namespace BioTek.Application.Services;

public class CatalogoService : ICatalogoService
{
    private readonly IRepository<Catalogo> _catalogoRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public CatalogoService(
        IRepository<Catalogo> catalogoRepository,
        IRepository<Livro> livroRepository,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _catalogoRepository = catalogoRepository;
        _livroRepository = livroRepository;
        _httpClient = httpClient;
        _configuration = configuration;
    }

    // ========== CRUD BÁSICO ==========

    public async Task<IEnumerable<Catalogo>> GetAllAsync()
    {
        return await _catalogoRepository.GetAllAsync();
    }

    public async Task<Catalogo?> GetByIdAsync(string id)
    {
        return await _catalogoRepository.GetByIdAsync(id);
    }

    public async Task<Catalogo> CreateAsync(Catalogo catalogo)
    {
        catalogo.Id = Guid.NewGuid().ToString();
        catalogo.DataCriacao = DateTime.UtcNow;
        await _catalogoRepository.AddAsync(catalogo);
        return catalogo;
    }

    public async Task<Catalogo?> UpdateAsync(string id, Catalogo catalogo)
    {
        var existente = await _catalogoRepository.GetByIdAsync(id);
        if (existente == null) return null;

        catalogo.Id = id;
        catalogo.DataCriacao = existente.DataCriacao;
        catalogo.DataAtualizacao = DateTime.UtcNow;
        
        await _catalogoRepository.UpdateAsync(id, catalogo);
        return catalogo;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var existente = await _catalogoRepository.GetByIdAsync(id);
        if (existente == null) return false;
        
        await _catalogoRepository.DeleteAsync(id);
        return true;
    }

    // ========== BUSCAS ==========

    public async Task<IEnumerable<Catalogo>> SearchAsync(string termo)
    {
        if (string.IsNullOrWhiteSpace(termo))
            return await _catalogoRepository.GetAllAsync();

        return await _catalogoRepository.FindAsync(l =>
            l.Titulo.ToLower().Contains(termo.ToLower()) ||
            l.Autor.ToLower().Contains(termo.ToLower()) ||
            (l.CodigoBarras != null && l.CodigoBarras.Contains(termo)));
    }

    public async Task<IEnumerable<Catalogo>> SearchByGeneroAsync(string genero)
    {
        if (string.IsNullOrWhiteSpace(genero))
            return await _catalogoRepository.GetAllAsync();

        return await _catalogoRepository.FindAsync(l =>
            l.Generos.Any(g => g.ToLower().Contains(genero.ToLower())));
    }

    public async Task<IEnumerable<Catalogo>> GetMaisRecentesAsync(int limite)
    {
        var todos = await _catalogoRepository.GetAllAsync();
        return todos.OrderByDescending(l => l.DataCriacao).Take(limite);
    }

    // ========== INTEGRAÇÃO COM APIs EXTERNAS ==========

    public async Task<Catalogo?> BuscarPorIsbnAsync(string isbn)
    {
        // 1. Busca no catálogo local
        var existente = (await _catalogoRepository.FindAsync(c => c.CodigoBarras == isbn)).FirstOrDefault();
        if (existente != null)
            return existente;

        // 2. Busca nas APIs externas
        var catalogoExterno = await BuscarExternoPorIsbnAsync(isbn);
        if (catalogoExterno == null)
            return null;

        // 3. Salva no catálogo
        return await CreateAsync(catalogoExterno);
    }

    public async Task<Catalogo?> BuscarExternoPorIsbnAsync(string isbn)
    {
        // Tenta Google Books
        var livro = await BuscarGoogleBooksPorIsbnAsync(isbn);
        if (livro != null) return livro;

        // Tenta OpenLibrary
        livro = await BuscarOpenLibraryPorIsbnAsync(isbn);
        if (livro != null) return livro;

        return null;
    }

    public async Task<List<Catalogo>> BuscarExternoPorTextoAsync(string termo, int limite = 20)
    {
        var resultados = new List<Catalogo>();
        
        // Busca no Google Books
        var googleResultados = await BuscarGoogleBooksPorTextoAsync(termo, limite);
        resultados.AddRange(googleResultados);
        
        // Busca no OpenLibrary (complementar)
        if (resultados.Count < limite)
        {
            var openResultados = await BuscarOpenLibraryPorTextoAsync(termo, limite - resultados.Count);
            resultados.AddRange(openResultados);
        }
        
        return resultados.DistinctBy(l => l.CodigoBarras).Take(limite).ToList();
    }

    // ========== IMPORTAÇÃO ==========

    public async Task<ImportacaoResultado> ImportarPorTermoAsync(string termo, int limite = 50)
    {
        var resultado = new ImportacaoResultado();
        
        var livros = await BuscarExternoPorTextoAsync(termo, limite);
        resultado.TotalProcessados = livros.Count;

        foreach (var livro in livros)
        {
            var item = new ImportacaoItem
            {
                Titulo = livro.Titulo,
                Autor = livro.Autor,
                Isbn = livro.CodigoBarras
            };

            try
            {
                var existente = (await _catalogoRepository.FindAsync(c => 
                    c.CodigoBarras == livro.CodigoBarras)).FirstOrDefault();
                
                if (existente != null)
                {
                    item.Status = "JaExiste";
                    resultado.JaExistentes++;
                }
                else
                {
                    await CreateAsync(livro);
                    item.Status = "Importado";
                    resultado.Importados++;
                }
            }
            catch (Exception ex)
            {
                item.Status = "Erro";
                item.Mensagem = ex.Message;
                resultado.Erros++;
            }
            
            resultado.Itens.Add(item);
        }
        
        return resultado;
    }

    public async Task<ImportacaoResultado> ImportarPorIsbnsAsync(List<string> isbns)
    {
        var resultado = new ImportacaoResultado();
        resultado.TotalProcessados = isbns.Count;

        foreach (var isbn in isbns)
        {
            var item = new ImportacaoItem { Isbn = isbn };

            try
            {
                var catalogo = await BuscarPorIsbnAsync(isbn);
                
                if (catalogo != null)
                {
                    item.Titulo = catalogo.Titulo;
                    item.Autor = catalogo.Autor;
                    item.Status = "Importado";
                    resultado.Importados++;
                }
                else
                {
                    item.Status = "NaoEncontrado";
                    resultado.Erros++;
                }
            }
            catch (Exception ex)
            {
                item.Status = "Erro";
                item.Mensagem = ex.Message;
                resultado.Erros++;
            }
            
            resultado.Itens.Add(item);
        }
        
        return resultado;
    }

    public async Task<ImportacaoResultado> ImportarPopularesAsync(string categoria = "best-sellers", int limite = 50)
    {
        // Busca termos populares baseados na categoria
        var termos = categoria.ToLower() switch
        {
            "best-sellers" => new[] { "best sellers 2024", "top books 2024" },
            "classicos" => new[] { "classic literature", "machado de assis", "dom casmurro" },
            "fantasia" => new[] { "fantasy best sellers", "harry potter", "game of thrones" },
            "ficcao" => new[] { "fiction best sellers", "literatura brasileira" },
            "romance" => new[] { "romance books", "best romance novels" },
            _ => new[] { categoria }
        };

        var resultado = new ImportacaoResultado();
        
        foreach (var termo in termos)
        {
            var importacao = await ImportarPorTermoAsync(termo, limite / termos.Length);
            resultado.TotalProcessados += importacao.TotalProcessados;
            resultado.Importados += importacao.Importados;
            resultado.JaExistentes += importacao.JaExistentes;
            resultado.Erros += importacao.Erros;
            resultado.Itens.AddRange(importacao.Itens);
            
            if (resultado.Importados >= limite) break;
        }
        
        return resultado;
    }

    // ========== BIBLIOTECA PESSOAL ==========

    public async Task<Livro?> AdicionarABibliotecaPessoalAsync(string catalogoId, string usuarioId, int? status = null)
    {
        var catalogo = await _catalogoRepository.GetByIdAsync(catalogoId);
        if (catalogo == null)
            return null;

        // Verifica se o usuário já tem esse livro
        var existe = (await _livroRepository.FindAsync(l => 
            l.IdUsuario == usuarioId && l.CodigoBarras == catalogo.CodigoBarras)).FirstOrDefault();
        
        if (existe != null)
            throw new InvalidOperationException("Você já possui este livro na sua biblioteca");

        var novoLivro = new Livro
        {
            Id = Guid.NewGuid().ToString(),
            IdUsuario = usuarioId,
            Titulo = catalogo.Titulo,
            Autor = catalogo.Autor,
            Descricao = catalogo.Descricao,
            Imagem = catalogo.Imagem,
            CodigoBarras = catalogo.CodigoBarras,
            Status = status.HasValue ? (StatusLeitura)status.Value : StatusLeitura.QueroLer,
            DataCriacao = DateTime.UtcNow
        };

        await _livroRepository.AddAsync(novoLivro);
        return novoLivro;
    }

    // ========== ESTATÍSTICAS ==========

    public async Task<int> GetTotalCountAsync()
    {
        var todos = await _catalogoRepository.GetAllAsync();
        return todos.Count();
    }

    public async Task<List<string>> GetGenerosMaisComunsAsync(int limite = 10)
    {
        var todos = await _catalogoRepository.GetAllAsync();
        return todos
            .SelectMany(l => l.Generos)
            .GroupBy(g => g)
            .OrderByDescending(g => g.Count())
            .Take(limite)
            .Select(g => g.Key)
            .ToList();
    }

    // ========== MÉTODOS PRIVADOS - APIs EXTERNAS ==========

    private async Task<Catalogo?> BuscarGoogleBooksPorIsbnAsync(string isbn)
    {
        try
        {
            var apiKey = _configuration["BooksApi:GoogleApiKey"];
            if (string.IsNullOrEmpty(apiKey)) return null;

            var url = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn}&key={apiKey}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JsonDocument.Parse(response);

            if (!json.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
                return null;

            return MapearGoogleBooksVolume(items[0]);
        }
        catch
        {
            return null;
        }
    }

    private async Task<Catalogo?> BuscarOpenLibraryPorIsbnAsync(string isbn)
    {
        try
        {
            var url = $"https://openlibrary.org/search.json?isbn={isbn}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JsonDocument.Parse(response);

            if (!json.RootElement.TryGetProperty("docs", out var docs) || docs.GetArrayLength() == 0)
                return null;

            return MapearOpenLibraryDoc(docs[0], isbn);
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<Catalogo>> BuscarGoogleBooksPorTextoAsync(string termo, int limite)
    {
        var resultados = new List<Catalogo>();
        try
        {
            var apiKey = _configuration["BooksApi:GoogleApiKey"];
            if (string.IsNullOrEmpty(apiKey)) return resultados;

            var url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(termo)}&key={apiKey}&maxResults={limite}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JsonDocument.Parse(response);

            if (!json.RootElement.TryGetProperty("items", out var items))
                return resultados;

            foreach (var item in items.EnumerateArray())
            {
                var catalogo = MapearGoogleBooksVolume(item);
                if (catalogo != null && !string.IsNullOrEmpty(catalogo.Titulo))
                    resultados.Add(catalogo);
            }
        }
        catch { }

        return resultados;
    }

    private async Task<List<Catalogo>> BuscarOpenLibraryPorTextoAsync(string termo, int limite)
    {
        var resultados = new List<Catalogo>();
        try
        {
            var url = $"https://openlibrary.org/search.json?q={Uri.EscapeDataString(termo)}&limit={limite}";
            var response = await _httpClient.GetStringAsync(url);
            var json = JsonDocument.Parse(response);

            if (!json.RootElement.TryGetProperty("docs", out var docs))
                return resultados;

            foreach (var doc in docs.EnumerateArray().Take(limite))
            {
                var catalogo = MapearOpenLibraryDoc(doc);
                if (catalogo != null && !string.IsNullOrEmpty(catalogo.Titulo))
                    resultados.Add(catalogo);
            }
        }
        catch { }

        return resultados;
    }

    private Catalogo? MapearGoogleBooksVolume(JsonElement item)
    {
        try
        {
            var info = item.GetProperty("volumeInfo");
            
            var isbn = "";
            if (info.TryGetProperty("industryIdentifiers", out var ids))
            {
                foreach (var idItem in ids.EnumerateArray())
                {
                    if (idItem.TryGetProperty("type", out var type) && type.GetString() == "ISBN_13")
                    {
                        isbn = idItem.GetProperty("identifier").GetString() ?? "";
                        break;
                    }
                }
            }

            var categorias = new List<string>();
            if (info.TryGetProperty("categories", out var cats))
            {
                categorias = cats.EnumerateArray().Select(c => c.GetString() ?? "").ToList();
            }

            return new Catalogo
            {
                Titulo = info.GetProperty("title").GetString() ?? "",
                Autor = info.TryGetProperty("authors", out var a) && a.GetArrayLength() > 0
                    ? string.Join(", ", a.EnumerateArray().Select(x => x.GetString()))
                    : "Autor desconhecido",
                Descricao = info.TryGetProperty("description", out var d) ? d.GetString() : null,
                Imagem = info.TryGetProperty("imageLinks", out var img) && img.TryGetProperty("thumbnail", out var t)
                    ? t.GetString()
                    : null,
                CodigoBarras = isbn,
                Fonte = "GoogleBooks",
                FonteId = item.TryGetProperty("id", out var googleId) ? googleId.GetString() : "",
                Generos = categorias,
                AnoPublicacao = info.TryGetProperty("publishedDate", out var data) && data.GetString()?.Length >= 4
                    ? int.Parse(data.GetString()![..4])
                    : null,
                Editora = info.TryGetProperty("publisher", out var pub) ? pub.GetString() : null,
                NumeroPaginas = info.TryGetProperty("pageCount", out var pages) ? pages.GetInt32() : null,
                AvaliacaoMedia = info.TryGetProperty("averageRating", out var nota) ? nota.GetDouble() : null
            };
        }
        catch
        {
            return null;
        }
    }

    private Catalogo? MapearOpenLibraryDoc(JsonElement doc, string? isbnOverride = null)
    {
        try
        {
            var isbn = isbnOverride ?? (doc.TryGetProperty("isbn", out var i) && i.GetArrayLength() > 0
                ? i[0].GetString()
                : null);

            var generos = new List<string>();
            if (doc.TryGetProperty("subject", out var subs))
            {
                generos = subs.EnumerateArray().Select(s => s.GetString() ?? "").Take(5).ToList();
            }

            return new Catalogo
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
                Fonte = "OpenLibrary",
                FonteId = doc.TryGetProperty("key", out var key) ? key.GetString() : "",
                Generos = generos,
                AnoPublicacao = doc.TryGetProperty("first_publish_year", out var ano) ? ano.GetInt32() : null,
                Editora = doc.TryGetProperty("publisher", out var pub) && pub.GetArrayLength() > 0
                    ? pub[0].GetString()
                    : null
            };
        }
        catch
        {
            return null;
        }
    }
}