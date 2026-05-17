using System.Text.Json;
using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;




namespace BioTek.Application.Services;

public class LivroService : ILivroService
{
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<Colecao> _colecaoRepository;
    private readonly HttpClient _httpClient;

    public LivroService(IRepository<Livro> livroRepository, IRepository<Colecao> colecaoRepository, HttpClient httpClient)
    {
        _livroRepository = livroRepository;
        _colecaoRepository = colecaoRepository;
        _httpClient = httpClient;
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

    public async Task<Livro?> BuscarPorIsbnAsync(string isbn)
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