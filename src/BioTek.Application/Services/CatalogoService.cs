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
    private readonly IRepository<Usuario> _usuarioRepository;

    public CatalogoService(
        IRepository<Catalogo> catalogoRepository,
        IRepository<Livro> livroRepository,
        HttpClient httpClient,
        IConfiguration configuration,
        IRepository<Usuario> usuarioRepository)
    {
        _catalogoRepository = catalogoRepository;
        _livroRepository = livroRepository;
        _httpClient = httpClient;
        _configuration = configuration;
        _usuarioRepository = usuarioRepository;
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
        // Tenta OpenLibrary primeiro
        var livro = await BuscarOpenLibraryPorIsbnAsync(isbn);
        if (livro != null) return livro;

        // Fallback: Google Books
        livro = await BuscarGoogleBooksPorIsbnAsync(isbn);
        if (livro != null) return livro;

        return null;
    }

    public async Task<List<Catalogo>> BuscarExternoPorTextoAsync(string termo, int limite = 20)
    {
        var resultados = new List<Catalogo>();
        
        // Busca no OpenLibrary primeiro
        var openResultados = await BuscarOpenLibraryPorTextoAsync(termo, limite);
        resultados.AddRange(openResultados);
        
        // Complementa com Google Books
        if (resultados.Count < limite)
        {
            var googleResultados = await BuscarGoogleBooksPorTextoAsync(termo, limite - resultados.Count);
            resultados.AddRange(googleResultados);
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
        var termos = categoria.ToLower() switch
        {
            "terror" => new[] { 
                "horror books best sellers", "stephen king novels", "lovecraft fiction",
                "terror literature", "gothic horror novels", "supernatural horror books",
                "dean koontz books", "clive barker books", "joe hill horror",
                "anne rice vampire chronicles", "hp lovecraft cthulhu"
            },
            "fantasia" => new[] { 
                "fantasy best sellers", "harry potter", "game of thrones", 
                "lord of the rings", "brandon sanderson", "wheel of time",
                "tolkien middle earth", "robert jordan fantasy"
            },
            "romance" => new[] { 
                "romance books best", "best romance novels", "nicholas sparks",
                "jane austen novels", "romance best sellers contemporary"
            },
            "ficcao" => new[] { 
                "fiction best sellers", "literary fiction", "contemporary fiction",
                "award winning fiction", "booker prize winners"
            },
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
            
            await Task.Delay(500);
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


    public async Task<object> ImportarRobustoAsync(string? categoria, int livrosPorTermo = 40, int paginasPorTermo = 3)
    {
        var termos = ObterTermosPorCategoria(categoria ?? "tudo");
        var totalImportados = 0;
        var totalJaExistentes = 0;
        var totalProcessados = 0;
        var termosProcessados = 0;
        var erros = new List<object>();

        foreach (var termo in termos)
        {
            for (int pagina = 0; pagina < paginasPorTermo; pagina++)
            {
                try
                {
                    var startIndex = pagina * livrosPorTermo;
                    var livros = await BuscarGoogleBooksPaginadoAsync(termo, livrosPorTermo, startIndex);
                    
                    foreach (var livro in livros)
                    {
                        if (string.IsNullOrEmpty(livro.Titulo) || string.IsNullOrEmpty(livro.Autor))
                            continue;
                        
                        totalProcessados++;
                        
                        var existe = (await _catalogoRepository.FindAsync(c => c.CodigoBarras == livro.CodigoBarras)).Any();
                        
                        if (!existe && !string.IsNullOrEmpty(livro.Titulo))
                        {
                            await CreateAsync(livro);
                            totalImportados++;
                        }
                        else if (existe)
                        {
                            totalJaExistentes++;
                        }
                    }
                    
                    termosProcessados++;
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    erros.Add(new { termo, pagina, erro = ex.Message });
                }
            }
        }
        
        return new
        {
            Mensagem = "Importação robusta concluída",
            TermosProcessados = termosProcessados,
            TotalProcessados = totalProcessados,
            LivrosImportados = totalImportados,
            LivrosJaExistentes = totalJaExistentes,
            Erros = erros
        };
    }

    private List<string> ObterTermosPorCategoria(string categoria)
    {
        return categoria.ToLower() switch
        {
            "tudo" => new List<string>
            {
                // Best sellers por década
                "best sellers 1950", "best sellers 1960", "best sellers 1970",
                "best sellers 1980", "best sellers 1990", "best sellers 2000",
                "best sellers 2010", "best sellers 2020",
                
                // Prêmios literários
                "booker prize winners", "pulitzer prize fiction", "nobel prize literature",
                
                // Categorias populares
                "fantasy best sellers", "science fiction classics", "romance novels best",
                "thriller suspense books", "mystery detective novels", "horror books best",
                "biography memoirs best", "history books best",
                
                // Autores famosos
                "stephen king books", "agatha christie novels", "jane austen works",
                "george orwell books", "haruki murakami works",
                
                // Brasileiros
                "machado de assis obras", "jorge amado livros", "clarice lispector",
                "graciliano ramos", "guimaraes rosa", "cecilia meireles",
                "monteiro lobato", "carlos drummond de andrade",
                
                // Sagas famosas
                "harry potter series", "lord of the rings", "game of thrones",
                "chronicles of narnia", "the witcher series", "percy jackson series",
                
                // Ficção científica
                "isaac asimov foundation", "philip k dick novels", "arthur c clarke",
                "ray bradbury books", "frank herbert dune",
                
                // Autoajuda
                "personal development books", "business best sellers", "leadership books",
                "finance investing books", "psychology books best",
                
                // Infantojuvenil
                "children books classics", "young adult best sellers", "roald dahl books",
                
                // Quadrinhos
                "comic books best", "manga best sellers", "graphic novels award",
                "dc comics classics", "marvel comics best"
            },
            "brasileiros" => new List<string>
            {
                "machado de assis", "jorge amado", "clarice lispector", "graciliano ramos",
                "guimaraes rosa", "cecilia meireles", "monteiro lobato"
            },
            "classicos" => new List<string>
            {
                "classic literature", "great books", "world literature", "russian literature",
                "french literature", "english literature", "american classics"
            },
            "recentes" => new List<string>
            {
                "new york times best sellers 2024", "top books 2024", "best sellers 2024",
                "amazon best books 2024", "goodreads choice awards 2024"
            },
            _ => new List<string> { categoria }
        };
    }

    private async Task<List<Catalogo>> BuscarGoogleBooksPaginadoAsync(string termo, int maxResults, int startIndex)
    {
        var resultados = new List<Catalogo>();
        try
        {
            var apiKey = _configuration["BooksApi:GoogleApiKey"];
            if (string.IsNullOrEmpty(apiKey)) return resultados;

            var url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(termo)}&key={apiKey}&maxResults={maxResults}&startIndex={startIndex}";
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


        public async Task<object> ImportarPorLetrasAsync(int livrosPorLetra = 40, int atrasoMs = 500)
    {
        var letras = "abcdefghijklmnopqrstuvwxyz";
        var totalImportados = 0;
        var totalJaExistentes = 0;
        var resultadosPorLetra = new List<object>();

        foreach (var letra in letras)
        {
            var importadosNaLetra = 0;
            var existentesNaLetra = 0;
            
            try
            {
                var apiKey = _configuration["BooksApi:GoogleApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    return new { Erro = "API Key do Google Books não configurada" };
                }

                var url = $"https://www.googleapis.com/books/v1/volumes?q={letra}&key={apiKey}&maxResults={livrosPorLetra}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                if (!json.RootElement.TryGetProperty("items", out var items))
                {
                    resultadosPorLetra.Add(new { Letra = letra.ToString(), Importados = 0, JaExistentes = 0, Mensagem = "Nenhum item encontrado" });
                    continue;
                }

                foreach (var item in items.EnumerateArray())
                {
                    var volumeInfo = item.GetProperty("volumeInfo");
                    
                    var titulo = volumeInfo.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var autor = volumeInfo.TryGetProperty("authors", out var a) && a.GetArrayLength() > 0
                        ? string.Join(", ", a.EnumerateArray().Select(x => x.GetString()))
                        : "Autor desconhecido";
                    var descricao = volumeInfo.TryGetProperty("description", out var d) ? d.GetString() : null;
                    
                    var imagem = "";
                    if (volumeInfo.TryGetProperty("imageLinks", out var img) && img.TryGetProperty("thumbnail", out var thumb))
                        imagem = thumb.GetString() ?? "";

                    var isbn = "";
                    if (volumeInfo.TryGetProperty("industryIdentifiers", out var ids))
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

                    if (string.IsNullOrEmpty(titulo) || string.IsNullOrEmpty(autor))
                        continue;

                    // Verifica se já existe no catálogo
                    var existe = (await _catalogoRepository.FindAsync(c => c.CodigoBarras == isbn)).Any();
                    
                    if (!existe)
                    {
                        var novoCatalogo = new Catalogo
                        {
                            Id = Guid.NewGuid().ToString(),
                            Titulo = titulo,
                            Autor = autor,
                            Descricao = descricao,
                            Imagem = imagem,
                            CodigoBarras = isbn,
                            Fonte = "GoogleBooks",
                            DataCriacao = DateTime.UtcNow
                        };
                        
                        await _catalogoRepository.AddAsync(novoCatalogo);
                        importadosNaLetra++;
                    }
                    else
                    {
                        existentesNaLetra++;
                    }
                }
                
                totalImportados += importadosNaLetra;
                totalJaExistentes += existentesNaLetra;
                resultadosPorLetra.Add(new { Letra = letra.ToString(), Importados = importadosNaLetra, JaExistentes = existentesNaLetra });
                
                if (atrasoMs > 0)
                    await Task.Delay(atrasoMs);
            }
            catch (Exception ex)
            {
                resultadosPorLetra.Add(new { Letra = letra.ToString(), Erro = ex.Message });
            }
        }
        
        return new
        {
            Mensagem = "Importação por letras concluída",
            LetrasProcessadas = letras.Length,
            LivrosImportados = totalImportados,
            LivrosJaExistentes = totalJaExistentes,
            Detalhes = resultadosPorLetra
        };
    }

        public async Task<ImportacaoResultado> ImportarPorAssuntoOpenLibraryAsync(string assunto, int limite = 200, int maxPaginas = 5)
    {
        var resultado = new ImportacaoResultado();
        var totalPorPagina = 50; // OpenLibrary máximo por página
        
        for (int pagina = 1; pagina <= maxPaginas; pagina++)
        {
            try
            {
                var offset = (pagina - 1) * totalPorPagina;
                var url = $"https://openlibrary.org/subjects/{Uri.EscapeDataString(assunto)}.json?limit={totalPorPagina}&offset={offset}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                if (!json.RootElement.TryGetProperty("works", out var works) || works.GetArrayLength() == 0)
                    break; // sem mais resultados

                foreach (var work in works.EnumerateArray())
                {
                    try
                    {
                        var titulo = work.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                        var autores = work.TryGetProperty("authors", out var a) && a.GetArrayLength() > 0
                            ? a[0].TryGetProperty("name", out var name) ? name.GetString() ?? "Autor desconhecido" : "Autor desconhecido"
                            : "Autor desconhecido";
                        var coverId = work.TryGetProperty("cover_id", out var c) ? c.GetInt64().ToString() : null;

                        if (string.IsNullOrEmpty(titulo)) continue;

                        var existente = (await _catalogoRepository.FindAsync(c => 
                            c.Titulo == titulo && c.Autor == autores)).FirstOrDefault();

                        if (existente != null)
                        {
                            resultado.JaExistentes++;
                            continue;
                        }

                        var catalogo = new Catalogo
                        {
                            Titulo = titulo,
                            Autor = autores,
                            Imagem = coverId != null ? $"https://covers.openlibrary.org/b/id/{coverId}-L.jpg" : null,
                            Fonte = "OpenLibrary",
                            FonteId = work.TryGetProperty("key", out var k) ? k.GetString() : "",
                            Generos = new List<string> { assunto },
                            DataCriacao = DateTime.UtcNow
                        };

                        await CreateAsync(catalogo);
                        resultado.Importados++;
                        resultado.TotalProcessados++;

                        if (resultado.Importados >= limite)
                            break;
                    }
                    catch { }
                }

                if (resultado.Importados >= limite)
                    break;

                await Task.Delay(300); // respeitar rate limit
            }
            catch (Exception)
            {
                break;
            }
        }

        return resultado;
    }

    public async Task<List<object>> GetMaisAdicionadosAsync(int limite = 10)
    {
        // Busca todos os livros da biblioteca pessoal dos usuários
        var todosLivros = await _livroRepository.GetAllAsync();
        
        // Agrupa por título e conta quantos usuários têm cada livro
        var maisAdicionados = todosLivros
            .GroupBy(l => l.Titulo?.ToLower().Trim())
            .Select(g => new
            {
                Id = g.First().Id,  
                Titulo = g.First().Titulo,
                Autor = g.First().Autor,
                Imagem = g.First().Imagem,
                CodigoBarras = g.First().CodigoBarras,
                Quantidade = g.Count()
            })
            .OrderByDescending(x => x.Quantidade)
            .Take(limite)
            .ToList();
        
        return maisAdicionados.Cast<object>().ToList();
    }

    public async Task<object> GetBibliotecaInicialAsync(string usuarioId)
    {
        var catalogo = await _catalogoRepository.GetAllAsync();
        var maisAdicionados = await GetMaisAdicionadosAsync(10);
        var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
        
        // 🆕 Se não tem preferências, usa padrão
        var generosPreferidos = (usuario?.GenerosPreferidos != null && usuario.GenerosPreferidos.Count > 0)
            ? usuario.GenerosPreferidos
            : new List<string> { "Fantasia", "Romance", "Ficção" };
        
        // 🆕 Busca livros recomendados baseado nos gêneros
        var recomendados = await BuscarRecomendadosPorGeneros(generosPreferidos);
        
        return new
        {
            Catalogo = catalogo,
            MaisAdicionados = maisAdicionados,
            GenerosPreferidos = generosPreferidos, 
            Recomendados = recomendados,
            PrimeiroAcesso = usuario == null || (usuario.GenerosPreferidos == null || usuario.GenerosPreferidos.Count == 0)
        };
    }

    // 🆕 Método para buscar recomendados por gêneros
    private async Task<List<object>> BuscarRecomendadosPorGeneros(List<string> generos)
    {
        var todos = await _catalogoRepository.GetAllAsync();
        
        return todos
            .Where(l => l.Generos != null && l.Generos.Any(g => 
                generos.Any(gp => g.ToLower().Contains(gp.ToLower()))))
            .OrderBy(x => Guid.NewGuid()) // Aleatório
            .Take(10)
            .Select(l => new
            {
                l.Id,
                l.Titulo,
                l.Autor,
                l.Imagem,
                l.CodigoBarras,
                l.Generos
            })
            .Cast<object>()
            .ToList();
    }

    public async Task<List<string>> SalvarPreferenciasAsync(string usuarioId, List<string> generos)
    {
        var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario == null)
            throw new Exception("Usuário não encontrado");

        usuario.GenerosPreferidos = generos.Take(3).ToList();
        usuario.DataAtualizacao = DateTime.UtcNow;
        await _usuarioRepository.UpdateAsync(usuarioId, usuario);

        return usuario.GenerosPreferidos;
    }
}