using System;
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
                    await CheckInactiveReadersAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao verificar leitores inativos");
                }

                // Aguardar 24 horas antes da próxima verificação
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }

            _logger.LogInformation("ReadingReminderBackgroundService finalizado");
        }

        private async Task CheckInactiveReadersAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            
            var livroRepository = scope.ServiceProvider.GetRequiredService<IRepository<Livro>>();
            var firebaseService = scope.ServiceProvider.GetRequiredService<IFirebaseNotificationService>();

            // Buscar livros com status "Lendo" e sem atividade há 7 dias
            var livrosEmAndamento = await livroRepository.FindAsync(l => 
                l.Status == StatusLeitura.Lendo && 
                l.UltimaLeitura != null && 
                l.UltimaLeitura < DateTime.UtcNow.AddDays(-7));

            var totalLivros = livrosEmAndamento.Count();

            _logger.LogInformation($"Encontrados {totalLivros} livros em andamento sem atividade");

            foreach (var livro in livrosEmAndamento)
            {
                try
                {
                    _logger.LogInformation($"Enviando lembrete para usuário {livro.IdUsuario} sobre o livro '{livro.Titulo}'");
                    
                    var sucesso = await firebaseService.SendReadingReminderAsync(
                        livro.IdUsuario,
                        livro.Titulo,
                        livro.Id
                    );

                    if (sucesso)
                    {
                        _logger.LogInformation($"Lembrete enviado com sucesso para {livro.IdUsuario}");
                    }
                    else
                    {
                        _logger.LogWarning($"Falha ao enviar lembrete para {livro.IdUsuario}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erro ao processar livro {livro.Id}");
                }
            }
        }
    }
}