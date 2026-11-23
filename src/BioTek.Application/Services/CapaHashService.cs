using BioTek.Application.Helpers;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class CapaHashService : ICapaHashService
{
    private readonly IRepository<CapaHash> _capaHashRepository;
    private readonly IRepository<CapaHashControle> _controleRepository;
    private readonly IRepository<Catalogo> _catalogoRepository;
    private static readonly SemaphoreSlim _processamentoGate = new(1, 1);
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
        IRepository<CapaHashControle> controleRepository,
        IRepository<Catalogo> catalogoRepository,
        IRepository<Livro> livroRepository,
        HttpClient httpClient,
        ILogger<CapaHashService> logger)
    {
        _capaHashRepository = capaHashRepository;
        _controleRepository = controleRepository;
        _catalogoRepository = catalogoRepository;
        _livroRepository = livroRepository;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<int> ContarPendentesAsync(string fonte)
    {
        if (fonte == "Catalogo")
        {
            var estado = await SincronizarControlesAsync(
                _catalogoRepository, fonte, c => c.Id, c => c.Imagem);
            return estado.Controles.Values.Count(c =>
                c.Status == CapaHashControleStatus.Pendente ||
                (c.Status == CapaHashControleStatus.Erro &&
                 (c.ProximaTentativa == null || c.ProximaTentativa <= DateTime.UtcNow)));
        }

        if (fonte == "Livro")
        {
            var estado = await SincronizarControlesAsync(
                _livroRepository, fonte, l => l.Id, l => l.Imagem);
            return estado.Controles.Values.Count(c =>
                c.Status == CapaHashControleStatus.Pendente ||
                (c.Status == CapaHashControleStatus.Erro &&
                 (c.ProximaTentativa == null || c.ProximaTentativa <= DateTime.UtcNow)));
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

        await _processamentoGate.WaitAsync();
        try
        {
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
        finally
        {
            _processamentoGate.Release();
        }
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
        var estado = await SincronizarControlesAsync(repository, fonte, obterId, obterImagem);
        var agora = DateTime.UtcNow;
        var candidatos = estado.Controles.Values
            .Where(c => c.Status == CapaHashControleStatus.Pendente ||
                (c.Status == CapaHashControleStatus.Erro &&
                 (c.ProximaTentativa == null || c.ProximaTentativa <= agora)))
            .OrderBy(c => c.DataCriacao)
            .Take(limite ?? LIMITE_POPULAR)
            .ToList();

        var registrosPorId = estado.Registros.ToDictionary(obterId, StringComparer.Ordinal);
        var totalCriados = 0;

        _logger.LogInformation(
            "{Fonte}: {Total} controles; {Pendentes} elegíveis para processamento; limite: {Limite}",
            fonte, estado.Controles.Count, candidatos.Count, limite ?? LIMITE_POPULAR);

        await Parallel.ForEachAsync(
            candidatos,
            new ParallelOptions { MaxDegreeOfParallelism = PARALELISMO_MAX },
            async (controle, cancellationToken) =>
            {
                if (!registrosPorId.TryGetValue(controle.IdLivro, out var registro))
                    return;

                try
                {
                    var url = obterImagem(registro);
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        controle.Status = CapaHashControleStatus.SemImagem;
                        controle.UltimaTentativa = DateTime.UtcNow;
                        controle.ProximaTentativa = null;
                        controle.UltimoErro = null;
                        await _controleRepository.UpdateAsync(controle.Id, controle);
                        return;
                    }

                    var imageBytes = await BaixarImagemAsync(url);
                    if (imageBytes == null)
                        throw new HttpRequestException("Não foi possível baixar a imagem.");

                    var hashExistente = (await _capaHashRepository.FindAsync(c =>
                        c.Fonte == fonte && c.IdLivro == controle.IdLivro)).FirstOrDefault();

                    if (hashExistente == null)
                    {
                        var capaHash = criarHash(registro);
                        capaHash.Hash = PerceptualHash.Calcular(imageBytes);
                        await _capaHashRepository.AddAsync(capaHash);
                    }

                    controle.Status = CapaHashControleStatus.Processado;
                    controle.ImagemUrl = url;
                    controle.UltimaTentativa = DateTime.UtcNow;
                    controle.ProximaTentativa = null;
                    controle.UltimoErro = null;
                    await _controleRepository.UpdateAsync(controle.Id, controle);

                    System.Threading.Interlocked.Increment(ref totalCriados);
                }
                catch (Exception ex)
                {
                    controle.Status = CapaHashControleStatus.Erro;
                    controle.Tentativas++;
                    controle.UltimaTentativa = DateTime.UtcNow;
                    controle.ProximaTentativa = DateTime.UtcNow.AddHours(
                        Math.Min(24, Math.Pow(2, Math.Min(controle.Tentativas, 5))));
                    controle.UltimoErro = ex.Message.Length > 1000
                        ? ex.Message[..1000]
                        : ex.Message;
                    await _controleRepository.UpdateAsync(controle.Id, controle);

                    _logger.LogError(ex,
                        "Erro ao gerar hash da fonte {Fonte}, registro {Id}",
                        fonte, controle.IdLivro);
                    erros.Add($"{controle.IdLivro}: {ex.Message}");
                }
            });

        _logger.LogInformation("{Fonte}: {Criados} hashes criados; {Erros} falhas",
            fonte, totalCriados, erros.Count);
        return totalCriados;
    }

    private async Task<(List<T> Registros, Dictionary<string, CapaHashControle> Controles)>
        SincronizarControlesAsync<T>(
            IRepository<T> repository,
            string fonte,
            Func<T, string> obterId,
            Func<T, string?> obterImagem)
        where T : class
    {
        var registros = (await repository.GetAllAsync()).ToList();
        var hashes = (await _capaHashRepository.FindAsync(h => h.Fonte == fonte))
            .GroupBy(h => h.IdLivro, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var controles = (await _controleRepository.FindAsync(c => c.Fonte == fonte))
            .GroupBy(c => c.IdLivro, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var registro in registros)
        {
            var id = obterId(registro);
            var imagem = obterImagem(registro);
            var temImagem = !string.IsNullOrWhiteSpace(imagem);
            var temHash = hashes.TryGetValue(id, out var hashExistente);
            var controleExistente = controles.GetValueOrDefault(id);
            var imagemAnterior = hashExistente?.ImagemUrl ?? controleExistente?.ImagemUrl;

            if (temHash && (!temImagem || imagemAnterior != imagem))
            {
                await _capaHashRepository.DeleteAsync(hashExistente!.Id);
                hashes.Remove(id);
                temHash = false;
            }

            if (!controles.TryGetValue(id, out var controle))
            {
                controle = new CapaHashControle
                {
                    IdLivro = id,
                    Fonte = fonte,
                    Status = temHash
                        ? CapaHashControleStatus.Processado
                        : temImagem
                            ? CapaHashControleStatus.Pendente
                            : CapaHashControleStatus.SemImagem,
                    ImagemUrl = imagem,
                    UltimaTentativa = temHash ? DateTime.UtcNow : null
                };

                try
                {
                    await _controleRepository.AddAsync(controle);
                    controles[id] = controle;
                }
                catch (Exception ex)
                {
                    // Índice único protege contra criação concorrente por outra instância.
                    var existente = (await _controleRepository.FindAsync(c =>
                        c.Fonte == fonte && c.IdLivro == id)).FirstOrDefault();
                    if (existente == null) throw;
                    controles[id] = existente;
                    controle = existente;
                }
            }

            if (temHash && controle.Status != CapaHashControleStatus.Processado)
            {
                controle.Status = CapaHashControleStatus.Processado;
                controle.ImagemUrl = imagem;
                controle.ProximaTentativa = null;
                controle.UltimoErro = null;
                await _controleRepository.UpdateAsync(controle.Id, controle);
            }
            else if (!temHash &&
                (!temImagem ||
                 controle.Status == CapaHashControleStatus.SemImagem ||
                 controle.Status == CapaHashControleStatus.Processado ||
                 (!string.IsNullOrWhiteSpace(imagem) && controle.ImagemUrl != imagem)))
            {
                if (!temImagem)
                {
                    controle.Status = CapaHashControleStatus.SemImagem;
                    controle.ProximaTentativa = null;
                    controle.UltimoErro = null;
                }
                else
                {
                    controle.Status = CapaHashControleStatus.Pendente;
                    controle.Tentativas = 0;
                    controle.ProximaTentativa = null;
                    controle.UltimoErro = null;
                }

                controle.ImagemUrl = imagem;
                await _controleRepository.UpdateAsync(controle.Id, controle);
            }
        }

        var idsAtuais = registros.Select(obterId).ToHashSet(StringComparer.Ordinal);
        foreach (var controleObsoleto in controles.Values.Where(c => !idsAtuais.Contains(c.IdLivro)).ToList())
        {
            await _controleRepository.DeleteAsync(controleObsoleto.Id);
            controles.Remove(controleObsoleto.IdLivro);
        }

        return (registros, controles);
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