using BioTek.Application.Helpers;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class CapaHashService : ICapaHashService
{
    private readonly IRepository<CapaHash> _capaHashRepository;
    private readonly IRepository<Catalogo> _catalogoRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CapaHashService> _logger;

    // 🆕 Configurações
    private const double SIMILARIDADE_MINIMA = 0.75;
    private const int LIMITE_RESULTADOS = 10;
    private const int LIMITE_POPULAR = 500;           // Máx por vez
    private const int PARALELISMO_MAX = 3;           // Downloads paralelos
    private const int CACHE_MINUTOS = 30;             // Cache de hashes

    // 🆕 Cache em memória dos hashes
    private static List<CapaHash>? _cacheHashes;
    private static DateTime _cacheTime = DateTime.MinValue;
    private static readonly object _cacheLock = new();

    public CapaHashService(
        IRepository<CapaHash> capaHashRepository,
        IRepository<Catalogo> catalogoRepository,
        IRepository<Livro> livroRepository,
        HttpClient httpClient,
        ILogger<CapaHashService> logger)
    {
        _capaHashRepository = capaHashRepository;
        _catalogoRepository = catalogoRepository;
        _livroRepository = livroRepository;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<int> ContarPendentesAsync(string fonte)
    {
        if (fonte == "Catalogo")
        {
            var idsProcessados = (await _capaHashRepository.FindAsync(c => c.Fonte == "Catalogo"))
                .Select(c => c.IdLivro).ToHashSet();
            var total = await _catalogoRepository.CountAsync(_ => true);
            return Math.Max(0, checked((int)total) - idsProcessados.Count);
        }

        if (fonte == "Livro")
        {
            var idsProcessados = (await _capaHashRepository.FindAsync(c => c.Fonte == "Livro"))
                .Select(c => c.IdLivro).ToHashSet();
            var total = await _livroRepository.CountAsync(_ => true);
            return Math.Max(0, checked((int)total) - idsProcessados.Count);
        }

        return 0;
    }

    // ============ POPULAR ============

    public async Task<object> PopularAsync(int? limiteCatalogo = null, int? limiteLivros = null)
    {
        if (limiteCatalogo is < 0 or > LIMITE_POPULAR)
            throw new InvalidOperationException($"Limite do catálogo deve estar entre 0 e {LIMITE_POPULAR}");
        if (limiteLivros is < 0 or > LIMITE_POPULAR)
            throw new InvalidOperationException($"Limite de livros deve estar entre 0 e {LIMITE_POPULAR}");

        var errosCatalogo = new System.Collections.Concurrent.ConcurrentBag<string>();
        var errosLivros = new System.Collections.Concurrent.ConcurrentBag<string>();

        var totalCatalogo = await ProcessarFonteAsync(
            _catalogoRepository,
            "Catalogo",
            limiteCatalogo,
            c => c.Id,
            c => c.Imagem,
            c => new CapaHash
            {
                IdLivro = c.Id, Fonte = "Catalogo", Titulo = c.Titulo,
                Autor = c.Autor, ImagemUrl = c.Imagem, DataCriacao = DateTime.UtcNow
            },
            errosCatalogo);

        var totalLivros = await ProcessarFonteAsync(
            _livroRepository,
            "Livro",
            limiteLivros,
            l => l.Id,
            l => l.Imagem,
            l => new CapaHash
            {
                IdLivro = l.Id, Fonte = "Livro", Titulo = l.Titulo,
                Autor = l.Autor, ImagemUrl = l.Imagem, DataCriacao = DateTime.UtcNow
            },
            errosLivros);

        if (totalCatalogo + totalLivros > 0)
            InvalidarCache();

        return new
        {
            Mensagem = "População concluída",
            TotalCatalogo = totalCatalogo,
            TotalLivros = totalLivros,
            TotalGeral = totalCatalogo + totalLivros,
            ErrosCatalogo = errosCatalogo.Count,
            ErrosLivros = errosLivros.Count,
            DetalhesErrosCatalogo = errosCatalogo.Take(10).ToArray(),
            DetalhesErrosLivros = errosLivros.Take(10).ToArray()
        };
    }

    private async Task<int> ProcessarFonteAsync<T>(
        IRepository<T> repository,
        string fonte,
        int? limite,
        Func<T, string> obterId,
        Func<T, string?> obterImagem,
        Func<T, CapaHash> criarHash,
        System.Collections.Concurrent.ConcurrentBag<string> erros)
        where T : class
    {
        var idsProcessados = (await _capaHashRepository.FindAsync(c => c.Fonte == fonte))
            .Select(c => c.IdLivro).ToHashSet();
        var totalCriados = 0;
        var totalTentados = 0;
        var skip = 0;
        const int tamanhoPagina = 100;
        var maximo = limite ?? LIMITE_POPULAR;

        _logger.LogInformation("{Fonte}: {Total} hashes existentes; limite desta execução: {Limite}",
            fonte, idsProcessados.Count, maximo);

        while (totalTentados < maximo)
        {
            var pagina = (await repository.GetPaginadoAsync(
                _ => true, skip, tamanhoPagina, "DataCriacao", true)).ToList();
            if (pagina.Count == 0)
                break;

            skip += pagina.Count;
            var candidatos = pagina
                .Where(item => !idsProcessados.Contains(obterId(item)))
                .Take(maximo - totalTentados)
                .ToList();

            if (candidatos.Count > 0)
            {
                totalTentados += candidatos.Count;
                await Parallel.ForEachAsync(
                candidatos,
                new ParallelOptions { MaxDegreeOfParallelism = PARALELISMO_MAX },
                async (item, cancellationToken) =>
                {
                    try
                    {
                        var url = obterImagem(item);
                        if (string.IsNullOrWhiteSpace(url))
                        {
                            erros.Add($"{obterId(item)}: sem imagem");
                            return;
                        }

                        var imageBytes = await BaixarImagemAsync(url);
                        if (imageBytes == null)
                        {
                            erros.Add($"{obterId(item)}: erro ao baixar imagem");
                            return;
                        }

                        var capaHash = criarHash(item);
                        capaHash.Hash = PerceptualHash.Calcular(imageBytes);
                        await _capaHashRepository.AddAsync(capaHash);

                        System.Threading.Interlocked.Increment(ref totalCriados);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao gerar hash da fonte {Fonte}, registro {Id}",
                            fonte, obterId(item));
                        erros.Add($"{obterId(item)}: {ex.Message}");
                    }
                });
            }

            if (pagina.Count < tamanhoPagina)
                break;
        }

        _logger.LogInformation("{Fonte}: {Criados} hashes criados; {Erros} falhas",
            fonte, totalCriados, erros.Count);
        return totalCriados;
    }

    // ============ BUSCAR ============


    public async Task<List<object>> BuscarPorHashAsync(string imagemBase64)
    {
        if (string.IsNullOrEmpty(imagemBase64))
            return new List<object>();

        // 🆕 Validar formato
        if (!imagemBase64.StartsWith("data:image/"))
            return new List<object>();

        // 🆕 Gerar hash da imagem capturada
        var hashCapturado = await GerarHashAsync(imagemBase64);
        if (hashCapturado == null)
            return new List<object>();

        // 🆕 Usar cache
        var todosHashes = await ObterHashesComCacheAsync();
        
        if (!todosHashes.Any())
            return new List<object>();

        // 🆕 Calcular similaridade
        var resultados = new List<(CapaHash Capa, double Similaridade)>();

        foreach (var capa in todosHashes)
        {
            try
            {
                var similaridade = PerceptualHash.Similaridade(hashCapturado.Value, capa.Hash);
                
                if (similaridade >= SIMILARIDADE_MINIMA)
                {
                    resultados.Add((capa, similaridade));
                }
            }
            catch
            {
                // Ignorar erros individuais
            }
        }

        // 🆕 Ordenar por similaridade (maior primeiro) e pegar top N
        var top = resultados
            .OrderByDescending(r => r.Similaridade)
            .Take(LIMITE_RESULTADOS)
            .ToList();

        // 🆕 Buscar dados completos para cada resultado
        var resultadosCompletos = new List<object>();

        foreach (var r in top)
        {
            var dadosCompletos = await BuscarDadosCompletosAsync(r.Capa, r.Similaridade);
            if (dadosCompletos != null)
                resultadosCompletos.Add(dadosCompletos);
        }

        return resultadosCompletos;
    }

    // 🆕 Buscar dados completos do catálogo ou livro
    private async Task<object?> BuscarDadosCompletosAsync(CapaHash capa, double similaridade)
    {
        try
        {
            // ============ CATÁLOGO ============
            if (capa.Fonte == "Catalogo")
            {
                var catalogo = await _catalogoRepository.GetByIdAsync(capa.IdLivro);
                if (catalogo == null)
                {
                    // Fallback: usar dados da CapaHash
                    return new
                    {
                        capa.IdLivro,
                        capa.Fonte,
                        capa.Titulo,
                        capa.Autor,
                        ImagemUrl = capa.ImagemUrl,
                        Descricao = (string?)null,
                        Editora = (string?)null,
                        AnoPublicacao = (int?)null,
                        Generos = (List<string>?)null,
                        Idioma = (string?)null,
                        NumeroPaginas = (int?)null,
                        CodigoBarras = (string?)null,
                        AvaliacaoMedia = (double?)null,
                        Similaridade = Math.Round(similaridade * 100, 1)
                    };
                }

                // 🆕 Retorna todos os campos do Catálogo
                return new
                {
                    capa.IdLivro,
                    capa.Fonte,
                    catalogo.Titulo,
                    catalogo.Autor,
                    ImagemUrl = catalogo.Imagem,
                    catalogo.Descricao,
                    catalogo.Editora,
                    catalogo.AnoPublicacao,
                    catalogo.Generos,
                    catalogo.Idioma,
                    catalogo.NumeroPaginas,
                    catalogo.CodigoBarras,
                    catalogo.AvaliacaoMedia,
                    Similaridade = Math.Round(similaridade * 100, 1)
                };
            }
            
            // ============ LIVRO ============
            else if (capa.Fonte == "Livro")
            {
                var livro = await _livroRepository.GetByIdAsync(capa.IdLivro);
                if (livro == null)
                {
                    // Fallback: usar dados da CapaHash
                    return new
                    {
                        capa.IdLivro,
                        capa.Fonte,
                        capa.Titulo,
                        capa.Autor,
                        ImagemUrl = capa.ImagemUrl,
                        Descricao = (string?)null,
                        Editora = (string?)null,
                        AnoPublicacao = (int?)null,
                        Generos = (List<string>?)null,
                        Idioma = (string?)null,
                        NumeroPaginas = (int?)null,
                        CodigoBarras = (string?)null,
                        AvaliacaoMedia = (double?)null,
                        Similaridade = Math.Round(similaridade * 100, 1)
                    };
                }

                // 🆕 Retorna campos do Livro (sem AvaliacaoMedia - não existe)
                return new
                {
                    capa.IdLivro,
                    capa.Fonte,
                    livro.Titulo,
                    livro.Autor,
                    ImagemUrl = livro.Imagem,
                    livro.Descricao,
                    livro.Editora,
                    livro.AnoPublicacao,
                    livro.Generos,
                    livro.Idioma,
                    NumeroPaginas = livro.TotalPaginas,  // 🆕 Mapeado
                    livro.CodigoBarras,
                    AvaliacaoMedia = (double?)null,       // 🆕 Não existe no Livro
                    Similaridade = Math.Round(similaridade * 100, 1)
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erro ao buscar dados completos para {capa.IdLivro}");
            return null;
        }
    }

    // ============ GERAR HASH ============

    public async Task<long?> GerarHashAsync(string imagemBase64)
    {
        try
        {
            var base64Limpo = imagemBase64;
            if (base64Limpo.Contains(','))
            {
                base64Limpo = base64Limpo.Split(',')[1];
            }

            var imageBytes = Convert.FromBase64String(base64Limpo);
            return PerceptualHash.Calcular(imageBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar hash da imagem");
            return null;
        }
    }

    // ============ LIMPAR ============

    public async Task<int> LimparTodosAsync()
    {
        var todos = await _capaHashRepository.GetAllAsync();
        var total = 0;

        foreach (var capa in todos)
        {
            await _capaHashRepository.DeleteAsync(capa.Id);
            total++;
        }

        // 🆕 Invalidar cache
        InvalidarCache();

        _logger.LogInformation($"Limpeza concluída: {total} hashes removidos");
        return total;
    }

    // ============ AUXILIARES ============

    private async Task<byte[]?> BaixarImagemAsync(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _httpClient.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }

    // 🆕 Cache de hashes em memória
    private async Task<List<CapaHash>> ObterHashesComCacheAsync()
    {
        lock (_cacheLock)
        {
            if (_cacheHashes != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromMinutes(CACHE_MINUTOS))
            {
                return _cacheHashes;
            }
        }

        var hashes = (await _capaHashRepository.GetAllAsync()).ToList();

        lock (_cacheLock)
        {
            _cacheHashes = hashes;
            _cacheTime = DateTime.UtcNow;
        }

        return hashes;
    }

    // 🆕 Invalidar cache
    private void InvalidarCache()
    {
        lock (_cacheLock)
        {
            _cacheHashes = null;
            _cacheTime = DateTime.MinValue;
        }
    }
}