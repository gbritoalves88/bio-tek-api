using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class StreakService : IStreakService
{
    private readonly IRepository<StreakUsuario> _streakRepository;
    private readonly IRepository<ComentarioLivro> _comentarioRepository;
    private readonly ILogger<StreakService> _logger;
    private readonly IRepository<Livro> _livroRepository;

    private static readonly TimeZoneInfo FusoBrasilia = 
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    // 🆕 Cache do streak
    private static readonly Dictionary<string, (object Data, DateTime Expiry)> _cache = new();
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromSeconds(10);

    public StreakService(
        IRepository<StreakUsuario> streakRepository,
        IRepository<ComentarioLivro> comentarioRepository,
        ILogger<StreakService> logger,
        IRepository<Livro> livroRepository)
    {
        _streakRepository = streakRepository;
        _comentarioRepository = comentarioRepository;
        _logger = logger;
        _livroRepository = livroRepository;
    }

    private DateTime AgoraBrasilia() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, FusoBrasilia);

    // 🆕 Limpa cache do usuário
    public void LimparCache(string usuarioId)
    {
        lock (_cacheLock)
        {
            _cache.Remove(usuarioId);
        }
    }

    public async Task RegistrarAtividadeAsync(string usuarioId)
    {
        try
        {
            var agora = AgoraBrasilia();
            var hoje = agora.Date;

            var streaks = await _streakRepository.FindAsync(s => s.IdUsuario == usuarioId);
            var streak = streaks.FirstOrDefault();

            if (streak == null)
            {
                streak = new StreakUsuario
                {
                    Id = Guid.NewGuid().ToString(),
                    IdUsuario = usuarioId,
                    SequenciaAtual = 1,
                    MaiorSequencia = 1,
                    UltimaDataAtividade = hoje,
                    DataCriacao = DateTime.UtcNow
                };
                await _streakRepository.AddAsync(streak);
            }
            else
            {
                var ultimaData = streak.UltimaDataAtividade?.Date;

                if (ultimaData == hoje)
                {
                    return; // Já leu hoje
                }
                else if (ultimaData == hoje.AddDays(-1))
                {
                    streak.SequenciaAtual++;
                    streak.MaiorSequencia = Math.Max(streak.MaiorSequencia, streak.SequenciaAtual);
                }
                else
                {
                    streak.SequenciaAtual = 1;
                }

                streak.UltimaDataAtividade = hoje;
                streak.DataAtualizacao = DateTime.UtcNow;

                await _streakRepository.UpdateAsync(streak.Id, streak);
            }

            // 🆕 Limpa cache após atualizar
            LimparCache(usuarioId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao registrar atividade de streak");
        }
    }

    public async Task<object?> GetStreakAsync(string usuarioId)
    {
        // 🆕 Verifica cache
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(usuarioId, out var cached) && DateTime.UtcNow < cached.Expiry)
            {
                return cached.Data;
            }
        }

        var agora = AgoraBrasilia();
        var hoje = agora.Date;

        var streaks = await _streakRepository.FindAsync(s => s.IdUsuario == usuarioId);
        var streak = streaks.FirstOrDefault();

        object resultado;

        if (streak == null)
        {
            resultado = new
            {
                sequenciaAtual = 0,
                maiorSequencia = 0,
                ultimaDataAtividade = (string?)null,
                leuHoje = false
            };
        }
        else
        {
            resultado = new
            {
                sequenciaAtual = streak.SequenciaAtual,
                maiorSequencia = streak.MaiorSequencia,
                ultimaDataAtividade = streak.UltimaDataAtividade?.ToString("yyyy-MM-dd"),
                leuHoje = streak.UltimaDataAtividade?.Date == hoje
            };
        }

        // 🆕 Salva no cache
        lock (_cacheLock)
        {
            _cache[usuarioId] = (resultado, DateTime.UtcNow.Add(CACHE_DURATION));
        }

        return resultado;
    }

    public async Task<object?> GetHistoricoAsync(string usuarioId)
    {
        var agora = AgoraBrasilia();
        var trintaDias = agora.Date.AddDays(-30);

        var atividades = await _comentarioRepository.FindAsync(c =>
            c.IdUsuario == usuarioId &&
            c.Origem == "Atividade" &&
            c.DataCriacao >= trintaDias);

        var dias = atividades
            .Select(a => TimeZoneInfo.ConvertTimeFromUtc(a.DataCriacao, FusoBrasilia).Date)
            .Distinct()
            .OrderByDescending(d => d)
            .Select(d => d.ToString("yyyy-MM-dd"))
            .ToList();

        return new { dias };
    }

    public async Task<object?> GetMetricasMesAsync(string usuarioId)
    {
        // 🆕 Usa Brasília para início do mês
        var agora = AgoraBrasilia();
        var inicioMes = new DateTime(agora.Year, agora.Month, 1);
        var inicioMesUtc = TimeZoneInfo.ConvertTimeToUtc(inicioMes, FusoBrasilia);

        var livros = (await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId)).ToList();
        var atividades = await _comentarioRepository.FindAsync(c =>
            c.IdUsuario == usuarioId &&
            c.Origem == "Atividade" &&
            c.DataCriacao >= inicioMesUtc);

        var livrosLidos = livros.Count(l => l.Status == StatusLeitura.Lido);
        var paginasLidas = livros.Sum(l => l.PaginaAtual ?? 0);
        var minutosOuvidos = livros.Sum(l => l.MinutoAtual ?? 0);
        var diasDeLeitura = atividades
            .Select(a => TimeZoneInfo.ConvertTimeFromUtc(a.DataCriacao, FusoBrasilia).Date)
            .Distinct()
            .Count();

        return new
        {
            livrosLidos,
            paginasLidas,
            minutosOuvidos,
            diasDeLeitura
        };
    }
}