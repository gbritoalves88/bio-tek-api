using System.Text.Json;
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

    public LivroService(
        IRepository<Livro> livroRepository,
        IRepository<Colecao> colecaoRepository,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _livroRepository = livroRepository;
        _colecaoRepository = colecaoRepository;
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<IEnumerable<Livro>> GetPorUsuarioAsync(string usuarioId)
    {
        return await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId);
    }

    public async Task<Livro?> GetPorIdAsync(string id)
    {
        return await _livroRepository.GetByIdAsync(id);
    }

    public async Task<Livro> CriarAsync(Livro livro)
    {
        // Valida se a coleção existe (se informada)
        if (!string.IsNullOrEmpty(livro.IdColecao))
        {
            var colecao = await _colecaoRepository.GetByIdAsync(livro.IdColecao);
            if (colecao == null)
                throw new InvalidOperationException("Coleção não encontrada");
        }

        livro.Id = Guid.NewGuid().ToString();
        livro.DataCriacao = DateTime.UtcNow;
        livro.Status = StatusLeitura.QueroLer;

        await _livroRepository.AddAsync(livro);
        return livro;
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
                Status = StatusLeitura.QueroLer
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
                Status = StatusLeitura.QueroLer
            };
        }
        catch
        {
            return null;
        }
    }

     public async Task<Livro?> BuscarPorIsbnAsync(string isbn)
    {
        // 1. Open Library Books
        var livro = await BuscarOpenLibraryBooks(isbn);
        if (livro != null) return livro;

        // 2. Open Library Search
        livro = await BuscarOpenLibrarySearch(isbn);
        if (livro != null) return livro;

        // 3. Google Books
        var apiKey = _configuration["BooksApi:GoogleApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            livro = await BuscarGoogleBooks(isbn, apiKey);
            if (livro != null) return livro;
        }

        return null;
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
                Status = StatusLeitura.QueroLer
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


    public async Task<Livro?> AtualizarAsync(string id, Livro livro)
    {
        var existente = await _livroRepository.GetByIdAsync(id);
        if (existente == null) return null;

        // Valida se a coleção existe (se informada)
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

        await _livroRepository.UpdateAsync(id, livro);
        return livro;
    }

    public async Task<bool> DeletarAsync(string id)
    {
        var existente = await _livroRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _livroRepository.DeleteAsync(id);
        return true;
    }

    public async Task<IEnumerable<Livro>> GetTodosAsync()
    {
        return await _livroRepository.GetAllAsync();
    }

    
}