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
    private const int DELAY_MS = 200;
    private const int LIMITE_RESULTADOS = 10;
    private const int LIMITE_POPULAR = 500;           // Máx por vez
    private const int PARALELISMO_MAX = 10;           // Downloads paralelos
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

    // ============ POPULAR ============

    public async Task<object> PopularAsync(int? limiteCatalogo = null, int? limiteLivros = null)
    {
        // 🆕 Validar limites
        if (limiteCatalogo.HasValue && limiteCatalogo.Value > LIMITE_POPULAR)
            throw new InvalidOperationException($"Limite máximo: {LIMITE_POPULAR} por vez");
        
        if (limiteLivros.HasValue && limiteLivros.Value > LIMITE_POPULAR)
            throw new InvalidOperationException($"Limite máximo: {LIMITE_POPULAR} por vez");

        var totalCatalogo = 0;
        var totalLivros = 0;
        var errosCatalogo = new List<string>();
        var errosLivros = new List<string>();

        // ============ 1. POPULAR DO CATÁLOGO ============
        
        // 🆕 Carregar IDs já processados (UMA query)
        var idsCatalogoProcessados = (await _capaHashRepository.GetAllAsync())
            .Where(c => c.Fonte == "Catalogo")
            .Select(c => c.IdLivro)
            .ToHashSet();
        
        _logger.LogInformation($"📊 Catálogo: {idsCatalogoProcessados.Count} hashes já processados");

        // 🆕 Buscar todos os catálogos
        var todosCatalogos = (await _catalogoRepository.GetAllAsync())
            .Where(c => !idsCatalogoProcessados.Contains(c.Id))  // Filtrar já processados
            .ToList();
        
        _logger.LogInformation($"📊 Catálogo: {todosCatalogos.Count} livros para processar");
        
        // 🆕 Aplicar limite (pega os PRIMEIROS N não processados)
        if (limiteCatalogo.HasValue)
            todosCatalogos = todosCatalogos.Take(limiteCatalogo.Value).ToList();
        
        _logger.LogInformation($"📊 Catálogo: processando {todosCatalogos.Count} livros neste lote");

        // 🆕 Processar em paralelo
        var semaphore = new SemaphoreSlim(PARALELISMO_MAX);
        
        var tarefasCatalogo = todosCatalogos.Select(async catalogo =>
        {
            await semaphore.WaitAsync();
            try
            {
                if (string.IsNullOrEmpty(catalogo.Imagem))
                {
                    lock (errosCatalogo)
                        errosCatalogo.Add($"{catalogo.Titulo}: sem imagem");
                    return;
                }

                var imageBytes = await BaixarImagemAsync(catalogo.Imagem);
                if (imageBytes == null)
                {
                    lock (errosCatalogo)
                        errosCatalogo.Add($"{catalogo.Titulo}: erro ao baixar imagem");
                    return;
                }

                long hash;
                try
                {
                    hash = PerceptualHash.Calcular(imageBytes);
                }
                catch (Exception ex)
                {
                    lock (errosCatalogo)
                        errosCatalogo.Add($"{catalogo.Titulo}: erro ao gerar hash - {ex.Message}");
                    return;
                }

                var capaHash = new CapaHash
                {
                    IdLivro = catalogo.Id,
                    Fonte = "Catalogo",
                    Hash = hash,
                    Titulo = catalogo.Titulo,
                    Autor = catalogo.Autor,
                    ImagemUrl = catalogo.Imagem,
                    DataCriacao = DateTime.UtcNow
                };

                await _capaHashRepository.AddAsync(capaHash);
                
                lock (errosCatalogo)
                {
                    totalCatalogo++;
                    if (totalCatalogo % 50 == 0)
                        _logger.LogInformation($"Catálogo: {totalCatalogo} hashes gerados");
                }

                await Task.Delay(DELAY_MS);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao processar catálogo: {catalogo.Titulo}");
                lock (errosCatalogo)
                    errosCatalogo.Add($"{catalogo.Titulo}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tarefasCatalogo);

        // ============ 2. POPULAR DOS LIVROS ============
        
        // 🆕 Carregar IDs já processados
        var idsLivrosProcessados = (await _capaHashRepository.GetAllAsync())
            .Where(c => c.Fonte == "Livro")
            .Select(c => c.IdLivro)
            .ToHashSet();
        
        _logger.LogInformation($"📊 Livros: {idsLivrosProcessados.Count} hashes já processados");

        // 🆕 Buscar todos os livros
        var todosLivros = (await _livroRepository.GetAllAsync())
            .Where(l => !idsLivrosProcessados.Contains(l.Id))  // Filtrar já processados
            .ToList();
        
        _logger.LogInformation($"📊 Livros: {todosLivros.Count} livros para processar");
        
        // 🆕 Aplicar limite
        if (limiteLivros.HasValue)
            todosLivros = todosLivros.Take(limiteLivros.Value).ToList();
        
        _logger.LogInformation($"📊 Livros: processando {todosLivros.Count} livros neste lote");

        var tarefasLivros = todosLivros.Select(async livro =>
        {
            await semaphore.WaitAsync();
            try
            {
                if (string.IsNullOrEmpty(livro.Imagem))
                {
                    lock (errosLivros)
                        errosLivros.Add($"{livro.Titulo}: sem imagem");
                    return;
                }

                var imageBytes = await BaixarImagemAsync(livro.Imagem);
                if (imageBytes == null)
                {
                    lock (errosLivros)
                        errosLivros.Add($"{livro.Titulo}: erro ao baixar imagem");
                    return;
                }

                long hash;
                try
                {
                    hash = PerceptualHash.Calcular(imageBytes);
                }
                catch (Exception ex)
                {
                    lock (errosLivros)
                        errosLivros.Add($"{livro.Titulo}: erro ao gerar hash - {ex.Message}");
                    return;
                }

                var capaHash = new CapaHash
                {
                    IdLivro = livro.Id,
                    Fonte = "Livro",
                    Hash = hash,
                    Titulo = livro.Titulo,
                    Autor = livro.Autor,
                    ImagemUrl = livro.Imagem,
                    DataCriacao = DateTime.UtcNow
                };

                await _capaHashRepository.AddAsync(capaHash);
                
                lock (errosLivros)
                {
                    totalLivros++;
                    if (totalLivros % 50 == 0)
                        _logger.LogInformation($"Livros: {totalLivros} hashes gerados");
                }

                await Task.Delay(DELAY_MS);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao processar livro: {livro.Titulo}");
                lock (errosLivros)
                    errosLivros.Add($"{livro.Titulo}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tarefasLivros);

        // 🆕 Invalidar cache após popular
        InvalidarCache();

        return new
        {
            Mensagem = "População concluída",
            TotalCatalogo = totalCatalogo,
            TotalLivros = totalLivros,
            TotalGeral = totalCatalogo + totalLivros,
            ErrosCatalogo = errosCatalogo.Count,
            ErrosLivros = errosLivros.Count,
            // 🆕 Info sobre o que falta
            CatalogoProcessado = idsCatalogoProcessados.Count + totalCatalogo,
            CatalogoTotal = idsCatalogoProcessados.Count + todosCatalogos.Count,
            LivrosProcessado = idsLivrosProcessados.Count + totalLivros,
            LivrosTotal = idsLivrosProcessados.Count + todosLivros.Count,
            DetalhesErrosCatalogo = errosCatalogo.Take(10),
            DetalhesErrosLivros = errosLivros.Take(10)
        };
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

        var top = resultados
            .OrderByDescending(r => r.Similaridade)
            .Take(LIMITE_RESULTADOS)
            .ToList();

        return top.Select(r => (object)new
        {
            r.Capa.IdLivro,
            r.Capa.Fonte,
            r.Capa.Titulo,
            r.Capa.Autor,
            r.Capa.ImagemUrl,
            Similaridade = Math.Round(r.Similaridade * 100, 1)
        }).ToList();
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
            var response = await _httpClient.GetAsync(url, cts.Token);
            
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