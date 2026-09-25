using System;
using System.Collections.Generic;
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

    private const int HORA_ENVIO = 18;

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

                var jaExecutou = await JaExecutouHoje("StreakNotification", hoje);

                if (!jaExecutou && agoraBrasilia.Hour >= HORA_ENVIO)
                {
                    await EnviarLembretesStreak();
                    _logger.LogInformation($"Lembretes de streak enviados e registrados em {agoraBrasilia}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao verificar streaks para notificação");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }

        _logger.LogInformation("StreakNotificationBackgroundService finalizado");
    }

    private async Task<bool> JaExecutouHoje(string jobName, DateTime data)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<JobExecution>>();
        var execucoes = await repo.FindAsync(j =>
            j.JobName == jobName &&
            j.DataExecucao == data);
        return execucoes.Any();
    }

    private async Task RegistrarExecucao(
        string jobName,
        DateTime data,
        DateTime dataHora)
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
        var notificacaoService = scope.ServiceProvider.GetRequiredService<INotificacaoService>(); // 🆕
        var notificacaoInternaRepo = scope.ServiceProvider.GetRequiredService<IRepository<Notificacao>>();

        var agoraBrasilia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, FusoBrasilia);
        var hoje = agoraBrasilia.Date;

        _logger.LogInformation("Verificando streaks para lembrete...");

        var streaks = await streakRepository.FindAsync(s => s.SequenciaAtual > 0);
        _logger.LogInformation($"Total de streaks ativos encontrados: {streaks.Count()}");

        var enviados = 0;

        foreach (var streak in streaks)
        {
            try
            {
                if (streak.UltimaDataAtividade?.Date == hoje)
                    continue;

                var usuario = await usuarioRepository.GetByIdAsync(streak.IdUsuario);
                if (usuario == null || !usuario.Ativo)
                    continue;

                var titulo = "🔥 Continue sua sequência!";
                var corpo = $"Sua sequência está em {streak.SequenciaAtual} dias. Leia um pouco hoje para continuar seu hábito.";
                var url = "/inicio";

                // 🆕 Envia o push via NotificacaoService (com cooldown/limite)
                var enviou = await notificacaoService.EnviarPushAsync(
                    streak.IdUsuario,
                    titulo,
                    corpo,
                    url,
                    "streak"
                );

                // 🆕 Cria notificação interna independente do push ter sido enviado
                await notificacaoInternaRepo.AddAsync(new Notificacao
                {
                    Id = Guid.NewGuid().ToString(),
                    IdUsuario = streak.IdUsuario,
                    Tipo = "streak",
                    Mensagem = corpo,
                    DataCriacao = DateTime.UtcNow,
                    Lida = false
                });

                if (enviou)
                {
                    enviados++;
                    _logger.LogInformation($"Lembrete de streak enviado para {usuario.Nickname} ({streak.SequenciaAtual} dias)");
                }
                else
                {
                    _logger.LogInformation($"Push de streak bloqueado para {usuario.Nickname} (cooldown/limite)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao enviar lembrete de streak para {streak.IdUsuario}");
            }
        }

        await RegistrarExecucao("StreakNotification", hoje, agoraBrasilia);

        _logger.LogInformation($"Total de lembretes de streak enviados: {enviados}");
    }
}