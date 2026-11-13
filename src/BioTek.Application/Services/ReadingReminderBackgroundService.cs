using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using BioTek.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services
{
    public class ReadingReminderBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ReadingReminderBackgroundService> _logger;

        private static readonly TimeZoneInfo FusoBrasilia =
            TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        private const int HORA_ENVIO = 19;

        public ReadingReminderBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<ReadingReminderBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReadingReminderBackgroundService iniciado");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var agoraBrasilia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, FusoBrasilia);
                    var hoje = agoraBrasilia.Date;

                    var jaExecutou = await JaExecutouHoje("ReadingReminder", hoje);

                    if (!jaExecutou && agoraBrasilia.Hour >= HORA_ENVIO)
                    {
                        await CheckInactiveReadersAsync();
                        _logger.LogInformation($"Lembretes de leitura enviados e registrados em {agoraBrasilia}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao verificar leitores inativos");
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }

            _logger.LogInformation("ReadingReminderBackgroundService finalizado");
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

        private async Task CheckInactiveReadersAsync()
        {
            using var scope = _serviceProvider.CreateScope();

            var livroRepository = scope.ServiceProvider.GetRequiredService<IRepository<Livro>>();
            var usuarioRepository = scope.ServiceProvider.GetRequiredService<IRepository<Usuario>>();
            var streakRepository = scope.ServiceProvider.GetRequiredService<IRepository<StreakUsuario>>();
            var notificacaoService = scope.ServiceProvider.GetRequiredService<INotificacaoService>();

            var agoraBrasilia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, FusoBrasilia);
            var hoje = agoraBrasilia.Date;
            var seteDias = agoraBrasilia.AddDays(-7);

            var streaksAtivos = await streakRepository.FindAsync(s => s.SequenciaAtual > 0);
            var usuariosComStreak = new HashSet<string>(streaksAtivos.Select(s => s.IdUsuario));

            _logger.LogInformation($"Usuários com streak ativo: {usuariosComStreak.Count}");

            var enviados = 0;

            // ============ 1. LIVROS "LENDO" PARADOS HÁ 7 DIAS ============
            var lendoParados = await livroRepository.FindAsync(l =>
                l.Status == StatusLeitura.Lendo &&
                l.UltimaLeitura != null &&
                l.UltimaLeitura < seteDias &&
                !usuariosComStreak.Contains(l.IdUsuario));

            var lendoPorUsuario = lendoParados
                .GroupBy(l => l.IdUsuario)
                .Select(g => g.OrderBy(x => Guid.NewGuid()).First())
                .ToList();

            foreach (var livro in lendoPorUsuario)
            {
                try
                {
                    var titulo = "📖 Continue sua leitura!";
                    var corpo = $"Você começou a ler '{livro.Titulo}' e está há um tempo sem atividade. Que tal continuar de onde parou?";
                    var url = $"/livros/{livro.Id}";

                    // 🆕 Apenas chama o Service. Ele cuida do push E da notificação interna.
                    var enviou = await notificacaoService.EnviarPushAsync(
                        livro.IdUsuario, titulo, corpo, url, "reading_reminder"
                    );

                    if (enviou) enviados++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erro ao processar livro {livro.Id}");
                }
            }

            // ============ 2. LIVROS "QUERO LER" HÁ 7 DIAS ============
            var queroLerParados = await livroRepository.FindAsync(l =>
                l.Status == StatusLeitura.QueroLer &&
                l.DataCriacao < seteDias &&
                !usuariosComStreak.Contains(l.IdUsuario));

            var queroLerPorUsuario = queroLerParados
                .GroupBy(l => l.IdUsuario)
                .Select(g => g.OrderBy(x => Guid.NewGuid()).First())
                .ToList();

            foreach (var livro in queroLerPorUsuario)
            {
                try
                {
                    var usuario = await usuarioRepository.GetByIdAsync(livro.IdUsuario);
                    if (usuario == null || !usuario.Ativo) continue;

                    var titulo = "📚 Comece sua leitura!";
                    var corpo = $"Você adicionou '{livro.Titulo}' mas ainda não começou. Que tal hoje?";
                    var url = $"/livros/{livro.Id}";

                    var enviou = await notificacaoService.EnviarPushAsync(
                        livro.IdUsuario, titulo, corpo, url, "start_reading"
                    );

                    if (enviou) enviados++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erro ao enviar 'Comece a ler' para {livro.IdUsuario}");
                }
            }

            // ============ 3. USUÁRIOS SEM LIVROS ============
            var todosUsuarios = await usuarioRepository.GetAllAsync();
            var todosLivros = await livroRepository.GetAllAsync();
            var usuariosSemLivro = todosUsuarios
                .Where(u => u.Ativo && !usuariosComStreak.Contains(u.Id))
                .Where(u => !todosLivros.Any(l => l.IdUsuario == u.Id))
                .ToList();

            foreach (var usuario in usuariosSemLivro)
            {
                try
                {
                    var titulo = "📕 Que tal começar?";
                    var corpo = "Adicione um livro no Bliteca e comece sua jornada de leitura!";
                    var url = "/inicio";

                    var enviou = await notificacaoService.EnviarPushAsync(
                        usuario.Id, titulo, corpo, url, "add_book"
                    );

                    if (enviou) enviados++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erro ao enviar 'Adicione um livro' para {usuario.Id}");
                }
            }

            await RegistrarExecucao("ReadingReminder", hoje, agoraBrasilia);

            _logger.LogInformation($"Total de lembretes de leitura enviados: {enviados}");
        }
    }
}