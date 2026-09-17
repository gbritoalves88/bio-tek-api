using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Models;
using BioTek.Models.DTOs;
using BioTek.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class NotificacaoService : INotificacaoService
{
    private readonly IRepository<Notificacao> _notificacaoRepository;
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<NotificationSubscription> _subscriptionRepository;
    private readonly IFirebaseNotificationService _firebaseService;
    private readonly ILogger<NotificacaoService> _logger;

    private static readonly Dictionary<string, (List<Notificacao> Data, DateTime Time)> _cacheNaoLidas = new();
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromSeconds(30);

    public NotificacaoService(
        IRepository<Notificacao> notificacaoRepository,
        IRepository<Usuario> usuarioRepository,
        IRepository<Livro> livroRepository,
        IRepository<NotificationSubscription> subscriptionRepository,
        IFirebaseNotificationService firebaseService,
        ILogger<NotificacaoService> logger)
    {
        _notificacaoRepository = notificacaoRepository;
        _usuarioRepository = usuarioRepository;
        _livroRepository = livroRepository;
        _subscriptionRepository = subscriptionRepository;
        _firebaseService = firebaseService;
        _logger = logger;
    }

    // ============ LEITURA ============

    public async Task<List<Notificacao>> GetNaoLidasAsync(string usuarioId)
    {
        if (string.IsNullOrEmpty(usuarioId))
            return new List<Notificacao>();

        lock (_cacheLock)
        {
            if (_cacheNaoLidas.TryGetValue(usuarioId, out var cached) &&
                DateTime.UtcNow - cached.Time < CACHE_DURATION)
            {
                return cached.Data;
            }
        }

        var notificacoes = await _notificacaoRepository.FindAsync(
            n => n.IdUsuario == usuarioId && !n.Lida);

        var resultado = notificacoes
            .OrderByDescending(n => n.DataCriacao)
            .Take(50)
            .ToList();

        lock (_cacheLock)
        {
            _cacheNaoLidas[usuarioId] = (resultado, DateTime.UtcNow);
        }

        return resultado;
    }

    public async Task<List<Notificacao>> GetAllAsync(string usuarioId)
    {
        if (string.IsNullOrEmpty(usuarioId))
            return new List<Notificacao>();

        var notificacoes = await _notificacaoRepository.FindAsync(
            n => n.IdUsuario == usuarioId);

        return notificacoes
            .OrderByDescending(n => n.DataCriacao)
            .Take(100)
            .ToList();
    }

    public async Task MarcarComoLidasAsync(string usuarioId)
    {
        if (string.IsNullOrEmpty(usuarioId))
            throw new ArgumentException("ID do usuário é obrigatório");

        var naoLidas = await _notificacaoRepository.FindAsync(
            n => n.IdUsuario == usuarioId && !n.Lida);

        if (!naoLidas.Any())
            return;

        foreach (var n in naoLidas)
        {
            n.Lida = true;
            n.DataAtualizacao = DateTime.UtcNow;
            await _notificacaoRepository.UpdateAsync(n.Id, n);
        }

        LimparCacheNaoLidas(usuarioId);
    }

    // ============ CRIAÇÃO + PUSH ============

    public async Task CriarNotificacaoSeguidor(string idUsuario, string idSeguidor, string nomeSeguidor)
    {
        if (string.IsNullOrEmpty(idUsuario) || string.IsNullOrEmpty(idSeguidor))
            return;

        var mensagem = $"{nomeSeguidor} começou a seguir você";

        var notificacao = new Notificacao
        {
            Id = Guid.NewGuid().ToString(),
            IdUsuario = idUsuario,
            Tipo = "seguidor",
            Mensagem = mensagem,
            IdOrigem = idSeguidor,
            DataCriacao = DateTime.UtcNow,
            Lida = false
        };
        await _notificacaoRepository.AddAsync(notificacao);
        LimparCacheNaoLidas(idUsuario);

        // 🆕 Push
        await EnviarPushAsync(idUsuario, "BliTeca", mensagem, "/inicio/comunidade", "seguidor");
    }

    public async Task CriarNotificacaoComentario(string idUsuarioDono, string idLivro, string tituloLivro, string nomeComentador)
    {
        if (string.IsNullOrEmpty(idUsuarioDono) || string.IsNullOrEmpty(idLivro))
            return;

        var mensagem = $"{nomeComentador} comentou em \"{tituloLivro}\"";

        var notificacao = new Notificacao
        {
            Id = Guid.NewGuid().ToString(),
            IdUsuario = idUsuarioDono,
            Tipo = "comentario",
            Mensagem = mensagem,
            IdOrigem = idLivro,
            DataCriacao = DateTime.UtcNow,
            Lida = false
        };
        await _notificacaoRepository.AddAsync(notificacao);
        LimparCacheNaoLidas(idUsuarioDono);

        // 🆕 Push
        await EnviarPushAsync(idUsuarioDono, "BliTeca", mensagem, $"/inicio/livro/{idLivro}", "comentario");
    }

    public async Task CriarNotificacaoCurtida(string idUsuarioDono, string idOrigem, string nomeCurtidor, string tipoAtividade, string tituloLivro)
    {
        if (string.IsNullOrEmpty(idUsuarioDono) || string.IsNullOrEmpty(idOrigem))
            return;

        var tipo = tipoAtividade == "livro" ? "curtida_livro" : "curtida_avaliacao";

        var mensagem = tipoAtividade == "avaliacao"
            ? $"{nomeCurtidor} curtiu sua avaliação de \"{tituloLivro}\" ❤️"
            : $"{nomeCurtidor} curtiu sua postagem de \"{tituloLivro}\" ❤️";

        var notificacao = new Notificacao
        {
            Id = Guid.NewGuid().ToString(),
            IdUsuario = idUsuarioDono,
            Tipo = tipo,
            Mensagem = mensagem,
            IdOrigem = idOrigem,
            DataCriacao = DateTime.UtcNow,
            Lida = false
        };
        await _notificacaoRepository.AddAsync(notificacao);
        LimparCacheNaoLidas(idUsuarioDono);

        // 🆕 Push
        await EnviarPushAsync(idUsuarioDono, "BliTeca", mensagem, "/inicio/comunidade", tipo);
    }

    // ============ SUBSCRIPTIONS ============

    public async Task<bool> SubscribeAsync(SubscribeRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.UserId) || string.IsNullOrEmpty(request?.Token))
                return false;

            var oldSubscriptions = await _subscriptionRepository.FindAsync(s =>
                s.UserId == request.UserId &&
                s.Device == request.Device &&
                s.IsActive);

            foreach (var old in oldSubscriptions)
            {
                old.IsActive = false;
                old.LastUsedAt = DateTime.UtcNow;
                await _subscriptionRepository.UpdateAsync(old.Id, old);
            }

            var existingSubscription = (await _subscriptionRepository.FindAsync(
                s => s.Token == request.Token)).FirstOrDefault();

            if (existingSubscription != null)
            {
                existingSubscription.UserId = request.UserId;
                existingSubscription.Device = request.Device;
                existingSubscription.Platform = request.Platform;
                existingSubscription.LastUsedAt = DateTime.UtcNow;
                existingSubscription.IsActive = true;

                await _subscriptionRepository.UpdateAsync(existingSubscription.Id, existingSubscription);
            }
            else
            {
                var subscription = new NotificationSubscription
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = request.UserId,
                    Token = request.Token,
                    Device = request.Device,
                    Platform = request.Platform,
                    LastUsedAt = DateTime.UtcNow,
                    IsActive = true
                };

                await _subscriptionRepository.AddAsync(subscription);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao registrar subscription");
            return false;
        }
    }

    public async Task<bool> UnsubscribeAsync(UnsubscribeRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.UserId) || string.IsNullOrEmpty(request?.Token))
                return false;

            var subscription = (await _subscriptionRepository.FindAsync(s =>
                s.Token == request.Token && s.UserId == request.UserId))
                .FirstOrDefault();

            if (subscription != null)
            {
                subscription.IsActive = false;
                subscription.LastUsedAt = DateTime.UtcNow;
                await _subscriptionRepository.UpdateAsync(subscription.Id, subscription);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao cancelar subscription");
            return false;
        }
    }

    public async Task<List<NotificationSubscription>> GetUserSubscriptionsAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return new List<NotificationSubscription>();

        var subscriptions = await _subscriptionRepository.FindAsync(s =>
            s.UserId == userId && s.IsActive);

        return subscriptions.ToList();
    }

    // ============ INCENTIVO ============

    public async Task CriarNotificacaoIncentivo(string idUsuario, string tipo, string mensagem)
    {
        if (string.IsNullOrEmpty(idUsuario) || string.IsNullOrEmpty(mensagem))
            return;

        var notificacao = new Notificacao
        {
            Id = Guid.NewGuid().ToString(),
            IdUsuario = idUsuario,
            Tipo = tipo,
            Mensagem = mensagem,
            DataCriacao = DateTime.UtcNow,
            Lida = false
        };
        await _notificacaoRepository.AddAsync(notificacao);
        LimparCacheNaoLidas(idUsuario);

        // 🆕 Push
        await EnviarPushAsync(idUsuario, "BliTeca", mensagem, "/inicio", tipo);
    }

    public async Task CriarNotificacaoStreak(string idUsuario, int streakAtual)
    {
        await CriarNotificacaoIncentivo(
            idUsuario,
            "streak",
            $"🔥 Sua sequência está em {streakAtual} dias! Continue lendo hoje!"
        );
    }

    public async Task CriarNotificacaoLeitura(string idUsuario, string tituloLivro, int diasParado)
    {
        await CriarNotificacaoIncentivo(
            idUsuario,
            "leitura",
            $"📚 Você está há {diasParado} dias sem ler '{tituloLivro}'. Continue de onde parou!"
        );
    }

    public async Task CriarNotificacaoIncentivoInicial(string idUsuario)
    {
        await CriarNotificacaoIncentivo(
            idUsuario,
            "incentivo",
            "📕 Adicione seu primeiro livro e comece sua jornada literária!"
        );
    }

    // ============ AUXILIARES ============

    private async Task EnviarPushAsync(string idUsuario, string titulo, string corpo, string url, string tipo)
    {
        try
        {
            var subs = await _subscriptionRepository.FindAsync(s =>
                s.UserId == idUsuario && s.IsActive);

            var tokens = subs.Select(s => s.Token).ToList();

            if (tokens.Count == 0)
                return;

            var data = new Dictionary<string, string>
            {
                { "type", tipo },
                { "url", url }
            };

            await _firebaseService.SendMulticastAsync(tokens, titulo, corpo, url, data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erro ao enviar push para usuário {idUsuario}");
        }
    }

    private void LimparCacheNaoLidas(string usuarioId)
    {
        lock (_cacheLock)
        {
            _cacheNaoLidas.Remove(usuarioId);
        }
    }
}