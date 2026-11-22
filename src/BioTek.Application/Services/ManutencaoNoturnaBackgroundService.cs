// BioTek.Application/Services/ManutencaoNoturnaBackgroundService.cs
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class ManutencaoNoturnaBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ManutencaoNoturnaBackgroundService> _logger;

    // 🆕 Configurações
    private const int HORA_EXECUCAO = 3;         // 3h da manhã (UTC)
    private const int LOTE_HASH = 100;           // 100 por lote
    private const int DELAY_ENTRE_LOTES_MS = 30000; // 30s entre lotes
    private const int DELAY_APOS_IMPORTACAO_MS = 300000; // 5 min após importação
    private static readonly SemaphoreSlim _execucaoGate = new(1, 1);
    public ManutencaoNoturnaBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ManutencaoNoturnaBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }


    public async Task ExecutarManualmenteAsync()
    {
        try
        {
            await ExecutarManutencaoAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro na execução manual");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🌙 ManutencaoNoturnaBackgroundService iniciado");

        // 🆕 Aguardar 5 min após startup (evita concorrência)
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 🆕 Calcular próxima execução (3h UTC)
                var agora = DateTime.UtcNow;
                var proximaExecucao = CalcularProximaExecucao(agora, HORA_EXECUCAO);
                var delay = proximaExecucao - agora;

                _logger.LogInformation($"⏰ Próxima execução em {delay.TotalHours:F1}h ({proximaExecucao:HH:mm} UTC)");

                await Task.Delay(delay, stoppingToken);

                // 🆕 Executar manutenção
                await ExecutarManutencaoAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("🛑 ManutencaoNoturnaBackgroundService parado");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro na manutenção noturna");
                
                // 🆕 Aguardar 1h antes de tentar de novo
                try
                {
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (TaskCanceledException) { break; }
            }
        }
    }

    private DateTime CalcularProximaExecucao(DateTime agora, int hora)
    {
        var proxima = new DateTime(agora.Year, agora.Month, agora.Day, hora, 0, 0, DateTimeKind.Utc);
        
        if (proxima <= agora)
            proxima = proxima.AddDays(1);
        
        return proxima;
    }

    private async Task ExecutarManutencaoAsync(CancellationToken stoppingToken)
    {
        await _execucaoGate.WaitAsync(stoppingToken);
        try
        {
        _logger.LogInformation("🌙 ========== INICIANDO MANUTENÇÃO NOTURNA ==========");
        var inicio = DateTime.UtcNow;

        using var scope = _serviceProvider.CreateScope();
        var catalogoService = scope.ServiceProvider.GetRequiredService<ICatalogoService>();
        var capaHashService = scope.ServiceProvider.GetRequiredService<ICapaHashService>();

        // ============ ETAPA 1: IMPORTAR LIVROS (GOOGLE + OPENLIBRARY) ============
        try
        {
            // 🆕 1a. Google Books - "tudo" (60+ termos)
            _logger.LogInformation("📚 Etapa 1a: Google Books (tudo)...");
            var resultadoGoogle = await catalogoService.ImportarRobustoAsync("tudo", 40, 3);
            _logger.LogInformation($"✅ Google Books concluído: {resultadoGoogle}");

            // 🆕 Aguardar entre APIs
            await Task.Delay(5000, stoppingToken);

            // 🆕 1b. OpenLibrary - subjects (14 assuntos)
            _logger.LogInformation("📚 Etapa 1b: OpenLibrary (subjects)...");
            
            var assuntos = new[]
            {
                // Ficção
                "fiction",
                "fantasy",
                "romance",
                "science_fiction",
                "mystery",
                "thriller",
                "horror",
                "adventure",
                
                // Não-ficção
                "biography",
                "history",
                "philosophy",
                "psychology",
                "poetry",
                
                // Brasil
                "brazilian_literature"
            };

            var totalOpenLibrary = 0;

            foreach (var assunto in assuntos)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    _logger.LogInformation($"   → Importando OpenLibrary: {assunto}");
                    
                    var resultado = await catalogoService.ImportarPorAssuntoOpenLibraryAsync(
                        assunto, 
                        limite: 100,      // 100 livros por assunto
                        maxPaginas: 3     // 3 páginas
                    );

                    totalOpenLibrary += resultado.Importados;
                    
                    _logger.LogInformation($"   ✅ {assunto}: {resultado.Importados} importados, {resultado.JaExistentes} já existiam");

                    // 🆕 Delay entre assuntos (evita bloqueio)
                    await Task.Delay(2000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"   ⚠️ Erro ao importar {assunto} (continuando...)");
                }
            }

            _logger.LogInformation($"✅ OpenLibrary concluído: {totalOpenLibrary} livros importados");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ Erro na importação de livros (continuando...)");
        }

        // 🆕 Aguardar 5 min antes de gerar hashes
        _logger.LogInformation($"⏸️ Aguardando {DELAY_APOS_IMPORTACAO_MS / 60000} min antes de gerar hashes...");
        try
        {
            await Task.Delay(DELAY_APOS_IMPORTACAO_MS, stoppingToken);
        }
        catch (TaskCanceledException) { return; }

        // ============ ETAPA 2: GERAR HASHES (CATÁLOGO) ============
        try
        {
            _logger.LogInformation("🖼️ Etapa 2: Gerando hashes do catálogo...");

            await ProcessarPendenciasAsync(capaHashService, "Catalogo", stoppingToken);

            _logger.LogInformation("✅ Catálogo concluído");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ Erro na geração de hashes do catálogo");
        }

        // ============ ETAPA 3: GERAR HASHES (LIVROS) ============
        try
        {
            _logger.LogInformation("📚 Etapa 3: Gerando hashes dos livros...");

            await ProcessarPendenciasAsync(capaHashService, "Livro", stoppingToken);

            _logger.LogInformation("✅ Livros concluído");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ Erro na geração de hashes dos livros");
        }

        // ============ RESUMO ============
        var duracao = DateTime.UtcNow - inicio;
        _logger.LogInformation($"🌙 ========== MANUTENÇÃO CONCLUÍDA em {duracao.TotalMinutes:F1} min ==========");
        }
        finally
        {
            _execucaoGate.Release();
        }
    }

    private async Task ProcessarPendenciasAsync(
        ICapaHashService capaHashService,
        string fonte,
        CancellationToken stoppingToken)
    {
        var pendentes = await capaHashService.ContarPendentesAsync(fonte);
        _logger.LogInformation("{Fonte}: {Pendentes} hashes pendentes", fonte, pendentes);

        while (pendentes > 0 && !stoppingToken.IsCancellationRequested)
        {
            var resultado = await capaHashService.PopularAsync(
                fonte == "Catalogo" ? LOTE_HASH : 0,
                fonte == "Livro" ? LOTE_HASH : 0);

            var novosPendentes = await capaHashService.ContarPendentesAsync(fonte);
            var processados = pendentes - novosPendentes;
            _logger.LogInformation(
                "{Fonte}: lote concluído. Pendentes antes: {Antes}; depois: {Depois}; avanço: {Avanco}. Resultado: {Resultado}",
                fonte, pendentes, novosPendentes, processados, resultado);

            if (processados <= 0)
            {
                _logger.LogWarning(
                    "{Fonte}: nenhum avanço no lote. Encerrando para evitar loop infinito; itens sem imagem ou com falha poderão ser tentados na próxima execução.",
                    fonte);
                break;
            }

            pendentes = novosPendentes;
            if (pendentes > 0)
                await Task.Delay(DELAY_ENTRE_LOTES_MS, stoppingToken);
        }
    }

    }
}