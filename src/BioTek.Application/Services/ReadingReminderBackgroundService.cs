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

        private const int HORA_ENVIO = 19; // 🆕 Enviar às 19h

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

                    // 🆕 Verifica se já executou hoje (persistido no MongoDB)
                    var jaExecutou = await JaExecutouHoje("ReadingReminder", hoje);

                    // 🆕 Só envia se:
                    // 1. Ainda não executou hoje
                    // 2. Está no horário ou depois das 19h
                    if (!jaExecutou && agoraBrasilia.Hour >= HORA_ENVIO)
                    {
                        await CheckInactiveReadersAsync();
                        await RegistrarExecucao("ReadingReminder", hoje, agoraBrasilia);
                        _logger.LogInformation($"Lembretes de leitura enviados e registrados em {agoraBrasilia}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao verificar leitores inativos");
                }

                // 🆕 Verificar a cada 1 hora (não 24 horas)
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }

            _logger.LogInformation("ReadingReminderBackgroundService finalizado");
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

        private async Task CheckInactiveReadersAsync()
        {
            using var scope = _serviceProvider.CreateScope();

            var livroRepository = scope.ServiceProvider.GetRequiredService<IRepository<Livro>>();
            var usuarioRepository = scope.ServiceProvider.GetRequiredService<IRepository<Usuario>>();
            var streakRepository = scope.ServiceProvider.GetRequiredService<IRepository<StreakUsuario>>();
            var firebaseService = scope.ServiceProvider.GetRequiredService<IFirebaseNotificationService>();
            var notificationRepository = scope.ServiceProvider.GetRequiredService<IRepository<NotificationSubscription>>();

            var agoraBrasilia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, FusoBrasilia);
            var seteDias = agoraBrasilia.AddDays(-7);

            // Buscar usuários COM streak ativo (para NÃO enviar)
            var streaksAtivos = await streakRepository.FindAsync(s => s.SequenciaAtual > 0);
            var usuariosComStreak = new HashSet<string>(streaksAtivos.Select(s => s.IdUsuario));

            _logger.LogInformation($"Usuários com streak ativo: {usuariosComStreak.Count}");

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

            _logger.LogInformation($"Livros 'Lendo' parados: {lendoPorUsuario.Count()}");

            foreach (var livro in lendoPorUsuario)
            {
                try
                {
                    _logger.LogInformation($"Enviando lembrete de leitura para {livro.IdUsuario} sobre '{livro.Titulo}'");

                    var sucesso = await firebaseService.SendReadingReminderAsync(
                        livro.IdUsuario,
                        livro.Titulo,
                        livro.Id
                    );

                    if (sucesso)
                        _logger.LogInformation($"Lembrete enviado para {livro.IdUsuario}");
                    else
                        _logger.LogWarning($"Falha ao enviar lembrete para {livro.IdUsuario}");
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

            _logger.LogInformation($"Livros 'Quero ler' parados: {queroLerPorUsuario.Count()}");

            foreach (var livro in queroLerPorUsuario)
            {
                try
                {
                    var usuario = await usuarioRepository.GetByIdAsync(livro.IdUsuario);
                    if (usuario == null || !usuario.Ativo)
                        continue;

                    var subscriptions = await notificationRepository.FindAsync(s =>
                        s.UserId == livro.IdUsuario && s.IsActive);

                    var tokens = subscriptions.Select(s => s.Token).ToList();

                    if (tokens.Count == 0)
                        continue;

                    _logger.LogInformation($"Enviando 'Comece a ler' para {livro.IdUsuario} sobre '{livro.Titulo}'");

                    await firebaseService.SendMulticastAsync(
                        tokens,
                        "📚 Comece sua leitura!",
                        $"Você adicionou '{livro.Titulo}' mas ainda não começou. Que tal hoje?",
                        $"/livros/{livro.Id}",
                        new Dictionary<string, string>
                        {
                            { "type", "start_reading" },
                            { "bookId", livro.Id }
                        }
                    );
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

            _logger.LogInformation($"Usuários sem livros: {usuariosSemLivro.Count}");

            foreach (var usuario in usuariosSemLivro)
            {
                try
                {
                    var subscriptions = await notificationRepository.FindAsync(s =>
                        s.UserId == usuario.Id && s.IsActive);

                    var tokens = subscriptions.Select(s => s.Token).ToList();

                    if (tokens.Count == 0)
                        continue;

                    _logger.LogInformation($"Enviando 'Adicione um livro' para {usuario.Id}");

                    await firebaseService.SendMulticastAsync(
                        tokens,
                        "📕 Que tal começar?",
                        "Adicione um livro no Bliteca e comece sua jornada de leitura!",
                        "/inicio",
                        new Dictionary<string, string>
                        {
                            { "type", "add_book" }
                        }
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erro ao enviar 'Adicione um livro' para {usuario.Id}");
                }
            }
        }
    }
}