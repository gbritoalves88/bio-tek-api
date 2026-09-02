using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class StreakNotificationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StreakNotificationBackgroundService> _logger;

    private static readonly TimeZoneInfo FusoBrasilia =
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private const int HORA_ENVIO = 18; // 🆕 Enviar às 18h

    public StreakNotificationBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<StreakNotificationBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StreakNotificationBackgroundService iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var agoraBrasilia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, FusoBrasilia);
                var hoje = agoraBrasilia.Date;

                // 🆕 Verifica se já executou hoje (persistido no MongoDB)
                var jaExecutou = await JaExecutouHoje("StreakNotification", hoje);

                // 🆕 Só envia se:
                // 1. Ainda não executou hoje
                // 2. Está no horário ou depois das 18h
                if (!jaExecutou && agoraBrasilia.Hour >= HORA_ENVIO)
                {
                    await EnviarLembretesStreak();
                    await RegistrarExecucao("StreakNotification", hoje, agoraBrasilia);
                    _logger.LogInformation($"Lembretes de streak enviados e registrados em {agoraBrasilia}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao verificar streaks para notificação");
            }

            // 🆕 Verificar a cada 1 hora (não 30 minutos)
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }

        _logger.LogInformation("StreakNotificationBackgroundService finalizado");
    }

    // 🆕 Verificar se já executou hoje
    private async Task<bool> JaExecutouHoje(string jobName, DateTime data)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<JobExecution>>();
        var execucoes = await repo.FindAsync(j =>
            j.JobName == jobName &&
            j.DataExecucao == data);
        return execucoes.Any();
    }

    // 🆕 Registrar execução no MongoDB
    private async Task RegistrarExecucao(string jobName, DateTime data, DateTime dataHora)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<JobExecution>>();
        await repo.AddAsync(new JobExecution
        {
            JobName = jobName,
            DataExecucao = data,
            UltimaExecucao = dataHora
        });
    }

    private async Task EnviarLembretesStreak()
    {
        using var scope = _serviceProvider.CreateScope();

        var streakRepository = scope.ServiceProvider.GetRequiredService<IRepository<StreakUsuario>>();
        var usuarioRepository = scope.ServiceProvider.GetRequiredService<IRepository<Usuario>>();
        var firebaseService = scope.ServiceProvider.GetRequiredService<IFirebaseNotificationService>();
        var notificacaoRepository = scope.ServiceProvider.GetRequiredService<IRepository<NotificationSubscription>>();

        var agoraBrasilia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, FusoBrasilia);
        var hoje = agoraBrasilia.Date;

        _logger.LogInformation("Verificando streaks para lembrete...");

        // Buscar todos os streaks ativos
        var streaks = await streakRepository.FindAsync(s => s.SequenciaAtual > 0);
        _logger.LogInformation($"Total de streaks ativos encontrados: {streaks.Count()}");

        var enviados = 0;

        foreach (var streak in streaks)
        {
            try
            {
                // Pular se já leu hoje
                if (streak.UltimaDataAtividade?.Date == hoje)
                {
                    continue;
                }

                // Verificar se o usuário está ativo
                var usuario = await usuarioRepository.GetByIdAsync(streak.IdUsuario);
                if (usuario == null || !usuario.Ativo)
                    continue;

                // Buscar tokens de notificação ativos
                var subscriptions = await notificacaoRepository.FindAsync(s =>
                    s.UserId == streak.IdUsuario && s.IsActive);

                var tokens = subscriptions.Select(s => s.Token).ToList();

                if (tokens.Count == 0)
                    continue;

                // Montar mensagem
                var titulo = "🔥 Continue sua sequência!";
                var corpo = $"Sua sequência está em {streak.SequenciaAtual} dias. Leia um pouco hoje para continuar seu hábito.";
                var url = "/inicio";

                var data = new Dictionary<string, string>
                {
                    { "type", "streak_reminder" },
                    { "streak", streak.SequenciaAtual.ToString() },
                    { "userId", streak.IdUsuario }
                };

                // Enviar para todos os dispositivos do usuário
                await firebaseService.SendMulticastAsync(tokens, titulo, corpo, url, data);
                enviados++;

                _logger.LogInformation($"Lembrete de streak enviado para usuário {streak.IdUsuario} (streak: {streak.SequenciaAtual})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao enviar lembrete de streak para {streak.IdUsuario}");
            }
        }

        _logger.LogInformation($"Total de lembretes de streak enviados: {enviados}");
    }
}