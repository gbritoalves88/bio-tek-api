using System.Linq.Expressions;
using System.Text.Json;
using BioTek.Domain.Common;
using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class CatalogoService : ICatalogoService
{
    private readonly IRepository<Catalogo> _catalogoRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IRepository<Usuario> _usuarioRepository;

    // Cache da biblioteca inicial
    private static object? _cacheBiblioteca;
    private static DateTime _cacheBibliotecaTime = DateTime.MinValue;
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(5);

    // Cache do catálogo completo
    private static List<Catalogo>? _cacheCatalogo;
    private static DateTime _cacheCatalogoTime = DateTime.MinValue;
    private static readonly TimeSpan CATALOGO_CACHE_DURATION = TimeSpan.FromMinutes(10);

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
        lock (_cacheLock)
        {
            if (_cacheCatalogo != null && DateTime.UtcNow - _cacheCatalogoTime < CATALOGO_CACHE_DURATION)
            {
                return _cacheCatalogo;
            }
        }

        var catalogos = (await _catalogoRepository.GetAllAsync()).ToList();

        lock (_cacheLock)
        {
            _cacheCatalogo = catalogos;
            _cacheCatalogoTime = DateTime.UtcNow;
        }

        return catalogos;
    }

    // 🆕 NOVO MÉTODO PAGINADO
    public async Task<PagedResult<Catalogo>> GetPaginadoAsync(int pagina, int tamanhoPagina, string? busca = null, string? genero = null)
    {
        if (pagina < 1) pagina = 1;
        if (tamanhoPagina < 1) tamanhoPagina = 20;
        if (tamanhoPagina > 100) tamanhoPagina = 100;

        Expression<Func<Catalogo, bool>> filtro = c => true;

        if (!string.IsNullOrWhiteSpace(busca) && !string.IsNullOrWhiteSpace(genero))
        {
            var termo = busca.Trim().ToLower();
            var gen = genero.Trim().ToLower();
            filtro = c => ((c.Titulo != null && c.Titulo.ToLower().Contains(termo)) ||
                           (c.Autor != null && c.Autor.ToLower().Contains(termo)))
                          && c.Generos != null && c.Generos.Any(g => g.ToLower() == gen);
        }
        else if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLower();
            filtro = c => (c.Titulo != null && c.Titulo.ToLower().Contains(termo)) ||
                          (c.Autor != null && c.Autor.ToLower().Contains(termo));
        }
        else if (!string.IsNullOrWhiteSpace(genero))
        {
            var gen = genero.Trim().ToLower();
            filtro = c => c.Generos != null && c.Generos.Any(g => g.ToLower() == gen);
        }

        var total = await _catalogoRepository.CountAsync(filtro);
        var skip = (pagina - 1) * tamanhoPagina;
        var itens = await _catalogoRepository.GetPaginadoAsync(filtro, skip, tamanhoPagina);

        return new PagedResult<Catalogo>
        {
            Total = total,
            Pagina = pagina,
            TamanhoPagina = tamanhoPagina,
            Itens = itens
        };
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
        LimparCacheCatalogo();
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
        LimparCacheCatalogo();
        return catalogo;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var existente = await _catalogoRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _catalogoRepository.DeleteAsync(id);
        LimparCacheCatalogo();
        return true;
    }

    // ========== BUSCAS ==========

    public async Task<IEnumerable<Catalogo>> SearchAsync(string termo)
    {
        if (string.IsNullOrWhiteSpace(termo))
            return await GetAllAsync();

        var todos = await GetAllAsync();
        var termoLower = termo.ToLower();

        return todos.Where(l =>
            l.Titulo.ToLower().Contains(termoLower) ||
            l.Autor.ToLower().Contains(termoLower) ||
            (l.CodigoBarras != null && l.CodigoBarras.Contains(termo)));
    }

    public async Task<IEnumerable<Catalogo>> SearchByGeneroAsync(string genero)
    {
        if (string.IsNullOrWhiteSpace(genero))
            return await GetAllAsync();

        var todos = await GetAllAsync();
        var generoLower = genero.ToLower();

        return todos.Where(l =>
            l.Generos != null && l.Generos.Any(g => g.ToLower().Contains(generoLower)));
    }

    public async Task<IEnumerable<Catalogo>> GetMaisRecentesAsync(int limite)
    {
        var todos = await GetAllAsync();
        return todos.OrderByDescending(l => l.DataCriacao).Take(limite);
    }

    // ========== INTEGRAÇÃO COM APIs EXTERNAS ==========

    public async Task<Catalogo?> BuscarPorIsbnAsync(string isbn)
    {
        var todos = await GetAllAsync();
        var existente = todos.FirstOrDefault(c => c.CodigoBarras == isbn);
        if (existente != null)
            return existente;

        var catalogoExterno = await BuscarExternoPorIsbnAsync(isbn);
        if (catalogoExterno == null)
            return null;

        return await CreateAsync(catalogoExterno);
    }

    public async Task<Catalogo?> BuscarExternoPorIsbnAsync(string isbn)
    {
        var livro = await BuscarOpenLibraryPorIsbnAsync(isbn);
        if (livro != null) return livro;

        livro = await BuscarGoogleBooksPorIsbnAsync(isbn);
        if (livro != null) return livro;

        return null;
    }

    public async Task<List<Catalogo>> BuscarExternoPorTextoAsync(string termo, int limite = 20)
    {
        var resultados = new List<Catalogo>();

        var openResultados = await BuscarOpenLibraryPorTextoAsync(termo, limite);
        resultados.AddRange(openResultados);

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

        var todos = (await GetAllAsync()).ToList();

        var isbnsExistentes = todos
            .Where(c => !string.IsNullOrEmpty(c.CodigoBarras))
            .Select(c => c.CodigoBarras!)
            .ToHashSet();

        var titulosAutoresExistentes = todos
            .Where(c => !string.IsNullOrEmpty(c.Titulo) && !string.IsNullOrEmpty(c.Autor))
            .Select(c => $"{c.Titulo.ToLower().Trim()}|{c.Autor.ToLower().Trim()}")
            .ToHashSet();

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
                var chave = $"{livro.Titulo?.ToLower().Trim()}|{livro.Autor?.ToLower().Trim()}";
                var jaExiste = false;

                if (!string.IsNullOrEmpty(livro.CodigoBarras) && isbnsExistentes.Contains(livro.CodigoBarras))
                {
                    jaExiste = true;
                }
                else if (titulosAutoresExistentes.Contains(chave))
                {
                    jaExiste = true;
                }

                if (jaExiste)
                {
                    item.Status = "JaExiste";
                    resultado.JaExistentes++;
                }
                else
                {
                    await CreateAsync(livro);
                    item.Status = "Importado";
                    resultado.Importados++;

                    if (!string.IsNullOrEmpty(livro.CodigoBarras))
                        isbnsExistentes.Add(livro.CodigoBarras);
                    titulosAutoresExistentes.Add(chave);
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
            Generos = catalogo.Generos ?? new List<string>(),
            AnoPublicacao = catalogo.AnoPublicacao,
            Editora = catalogo.Editora,
            Idioma = catalogo.Idioma,
            Status = status.HasValue ? (StatusLeitura)status.Value : StatusLeitura.QueroLer,
            DataCriacao = DateTime.UtcNow,
            Tipo = "livro"
        };

        await _livroRepository.AddAsync(novoLivro);
        return novoLivro;
    }

    // ========== ESTATÍSTICAS ==========

    public async Task<int> GetTotalCountAsync()
    {
        var todos = await GetAllAsync();
        return todos.Count();
    }

    public async Task<List<string>> GetGenerosMaisComunsAsync(int limite = 10)
    {
        var todos = await GetAllAsync();
        return todos
            .SelectMany(l => l.Generos ?? new List<string>())
            .GroupBy(g => g)
            .OrderByDescending(g => g.Count())
            .Take(limite)
            .Select(g => g.Key)
            .ToList();
    }

    // ========== BIBLIOTECA INICIAL (COM CACHE) ==========

    public async Task<List<object>> GetMaisAdicionadosAsync(int limite = 10)
    {
        var todosLivros = await _livroRepository.GetAllAsync();

        var maisAdicionados = todosLivros
            .GroupBy(l => l.Titulo?.ToLower().Trim())
            .Select(g => new
            {
                Id = g.First().Id,
                Titulo = g.First().Titulo,
                Autor = g.First().Autor,
                Imagem = g.First().Imagem,
                CodigoBarras = g.First().CodigoBarras,
                Generos = g.First().Generos,
                AnoPublicacao = g.First().AnoPublicacao,
                Editora = g.First().Editora,
                Idioma = g.First().Idioma,
                Quantidade = g.Count()
            })
            .OrderByDescending(x => x.Quantidade)
            .Take(limite)
            .ToList();

        return maisAdicionados.Cast<object>().ToList();
    }

    public async Task<object> GetBibliotecaInicialAsync(string usuarioId)
    {
        lock (_cacheLock)
        {
            if (_cacheBiblioteca != null && DateTime.UtcNow - _cacheBibliotecaTime < CACHE_DURATION)
            {
                return _cacheBiblioteca;
            }
        }

        var catalogo = await GetAllAsync();
        var maisAdicionados = await GetMaisAdicionadosAsync(10);
        var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);

        var generosPreferidos = (usuario?.GenerosPreferidos != null && usuario.GenerosPreferidos.Count > 0)
            ? usuario.GenerosPreferidos
            : new List<string> { "Fantasia", "Romance", "Ficção" };

        var recomendados = await BuscarRecomendadosPorGeneros(generosPreferidos);

        var resultado = new
        {
            Catalogo = catalogo,
            MaisAdicionados = maisAdicionados,
            GenerosPreferidos = generosPreferidos,
            Recomendados = recomendados,
            PrimeiroAcesso = usuario == null || (usuario.GenerosPreferidos == null || usuario.GenerosPreferidos.Count == 0)
        };

        lock (_cacheLock)
        {
            _cacheBiblioteca = resultado;
            _cacheBibliotecaTime = DateTime.UtcNow;
        }

        return resultado;
    }

    private async Task<List<object>> BuscarRecomendadosPorGeneros(List<string> generos)
    {
        var todos = await GetAllAsync();
        var generosLower = generos.Select(g => g.ToLower()).ToList();

        return todos
            .Where(l => l.Generos != null && l.Generos.Any(g =>
                generosLower.Any(gp => g.ToLower().Contains(gp))))
            .OrderBy(x => Guid.NewGuid())
            .Take(10)
            .Select(l => new
            {
                l.Id,
                l.Titulo,
                l.Autor,
                l.Imagem,
                l.CodigoBarras,
                l.Generos,
                l.AnoPublicacao,
                l.Editora,
                l.Idioma,
                l.NumeroPaginas
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

        lock (_cacheLock)
        {
            _cacheBiblioteca = null;
            _cacheBibliotecaTime = DateTime.MinValue;
        }

        return usuario.GenerosPreferidos;
    }

    // ========== MÉTODOS PRIVADOS ==========

    private void LimparCacheCatalogo()
    {
        lock (_cacheLock)
        {
            _cacheCatalogo = null;
            _cacheCatalogoTime = DateTime.MinValue;
            _cacheBiblioteca = null;
            _cacheBibliotecaTime = DateTime.MinValue;
        }
    }

    // ========== IMPORTAÇÃO ROBUSTA ==========

    public async Task<object> ImportarRobustoAsync(string? categoria, int livrosPorTermo = 40, int paginasPorTermo = 3)
    {
        var termos = ObterTermosPorCategoria(categoria ?? "tudo");
        var totalImportados = 0;
        var totalJaExistentes = 0;
        var totalProcessados = 0;
        var termosProcessados = 0;
        var erros = new List<object>();

        var todos = (await GetAllAsync()).ToList();

        var isbnsExistentes = todos
            .Where(c => !string.IsNullOrEmpty(c.CodigoBarras))
            .Select(c => c.CodigoBarras!)
            .ToHashSet();

        var titulosAutoresExistentes = todos
            .Where(c => !string.IsNullOrEmpty(c.Titulo) && !string.IsNullOrEmpty(c.Autor))
            .Select(c => $"{c.Titulo.ToLower().Trim()}|{c.Autor.ToLower().Trim()}")
            .ToHashSet();

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

                        var chave = $"{livro.Titulo.ToLower().Trim()}|{livro.Autor.ToLower().Trim()}";
                        var jaExiste = false;

                        if (!string.IsNullOrEmpty(livro.CodigoBarras) && isbnsExistentes.Contains(livro.CodigoBarras))
                        {
                            jaExiste = true;
                        }
                        else if (titulosAutoresExistentes.Contains(chave))
                        {
                            jaExiste = true;
                        }

                        if (!jaExiste)
                        {
                            await CreateAsync(livro);
                            totalImportados++;

                            if (!string.IsNullOrEmpty(livro.CodigoBarras))
                                isbnsExistentes.Add(livro.CodigoBarras);
                            titulosAutoresExistentes.Add(chave);
                        }
                        else
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
                "best sellers 1950", "best sellers 1960", "best sellers 1970",
                "best sellers 1980", "best sellers 1990", "best sellers 2000",
                "best sellers 2010", "best sellers 2020",
                "booker prize winners", "pulitzer prize fiction", "nobel prize literature",
                "fantasy best sellers", "science fiction classics", "romance novels best",
                "thriller suspense books", "mystery detective novels", "horror books best",
                "biography memoirs best", "history books best",
                "stephen king books", "agatha christie novels", "jane austen works",
                "george orwell books", "haruki murakami works",
                "machado de assis obras", "jorge amado livros", "clarice lispector",
                "graciliano ramos", "guimaraes rosa", "cecilia meireles",
                "monteiro lobato", "carlos drummond de andrade",
                "harry potter series", "lord of the rings", "game of thrones",
                "chronicles of narnia", "the witcher series", "percy jackson series",
                "isaac asimov foundation", "philip k dick novels", "arthur c clarke",
                "ray bradbury books", "frank herbert dune",
                "personal development books", "business best sellers", "leadership books",
                "finance investing books", "psychology books best",
                "children books classics", "young adult best sellers", "roald dahl books",
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

        var todos = (await GetAllAsync()).ToList();

        var isbnsExistentes = todos
            .Where(c => !string.IsNullOrEmpty(c.CodigoBarras))
            .Select(c => c.CodigoBarras!)
            .ToHashSet();

        var titulosAutoresExistentes = todos
            .Where(c => !string.IsNullOrEmpty(c.Titulo) && !string.IsNullOrEmpty(c.Autor))
            .Select(c => $"{c.Titulo.ToLower().Trim()}|{c.Autor.ToLower().Trim()}")
            .ToHashSet();

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

                    var chave = $"{titulo.ToLower().Trim()}|{autor.ToLower().Trim()}";
                    var jaExiste = false;

                    if (!string.IsNullOrEmpty(isbn) && isbnsExistentes.Contains(isbn))
                    {
                        jaExiste = true;
                    }
                    else if (titulosAutoresExistentes.Contains(chave))
                    {
                        jaExiste = true;
                    }

                    if (!jaExiste)
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

                        if (!string.IsNullOrEmpty(isbn))
                            isbnsExistentes.Add(isbn);
                        titulosAutoresExistentes.Add(chave);
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
        var totalPorPagina = 50;

        for (int pagina = 1; pagina <= maxPaginas; pagina++)
        {
            try
            {
                var offset = (pagina - 1) * totalPorPagina;
                var url = $"https://openlibrary.org/subjects/{Uri.EscapeDataString(assunto)}.json?limit={totalPorPagina}&offset={offset}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                if (!json.RootElement.TryGetProperty("works", out var works) || works.GetArrayLength() == 0)
                    break;

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

                await Task.Delay(300);
            }
            catch (Exception)
            {
                break;
            }
        }

        return resultado;
    }

    // ========== MAPEAMENTOS ==========

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
                Idioma = info.TryGetProperty("language", out var lang) ? lang.GetString() : null,
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
                Idioma = doc.TryGetProperty("language", out var lang) && lang.GetArrayLength() > 0
                    ? lang[0].GetString()
                    : null,
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