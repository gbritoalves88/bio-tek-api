using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Models;
using BioTek.Models.DTOs;
using BioTek.Services.Interfaces;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

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

    private static readonly HashSet<string> TiposSociais = new()
    {
        "seguidor", "comentario", "curtida_livro", "curtida_avaliacao"
    };

    private static readonly HashSet<string> TiposIncentivo = new()
    {
        "streak", "leitura", "reading_reminder", "start_reading", "add_book", "incentivo"
    };

    private const int LIMITE_DIARIO_INCENTIVO = 5;
    private static readonly TimeSpan COOLDOWN_SOCIAL = TimeSpan.FromHours(1);
    private static readonly TimeSpan JANELA_LIMITE_DIARIO = TimeSpan.FromHours(24);

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

    // ============ CRIAÇÃO (o EnviarPushAsync já cria a interna) ============

    public async Task CriarNotificacaoSeguidor(string idUsuario, string idSeguidor, string nomeSeguidor)
    {
        if (string.IsNullOrEmpty(idUsuario) || string.IsNullOrEmpty(idSeguidor))
            return;

        var mensagem = $"{nomeSeguidor} começou a seguir você";
        await EnviarPushAsync(idUsuario, "BliTeca", mensagem, "/inicio/comunidade", "seguidor");
    }

    public async Task CriarNotificacaoComentario(string idUsuarioDono, string idLivro, string tituloLivro, string nomeComentador)
    {
        if (string.IsNullOrEmpty(idUsuarioDono) || string.IsNullOrEmpty(idLivro))
            return;

        var mensagem = $"{nomeComentador} comentou em \"{tituloLivro}\"";
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

        await EnviarPushAsync(idUsuarioDono, "BliTeca", mensagem, "/inicio/comunidade", tipo);
    }

    public async Task CriarNotificacaoIncentivo(string idUsuario, string tipo, string mensagem)
    {
        if (string.IsNullOrEmpty(idUsuario) || string.IsNullOrEmpty(mensagem))
            return;

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
                s.IsActive &&
                s.Token != request.Token);

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

                try
                {
                    await _subscriptionRepository.AddAsync(subscription);
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    var sub = (await _subscriptionRepository.FindAsync(s =>
                        s.Token == request.Token)).FirstOrDefault();

                    if (sub != null)
                    {
                        sub.UserId = request.UserId;
                        sub.Device = request.Device;
                        sub.Platform = request.Platform;
                        sub.IsActive = true;
                        sub.LastUsedAt = DateTime.UtcNow;
                        await _subscriptionRepository.UpdateAsync(sub.Id, sub);
                    }
                }
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

    // ============ ENVIO DE PUSH (ÚNICO LUGAR que cria a Notificacao interna) ============

    public async Task<bool> EnviarPushAsync(
        string idUsuario,
        string titulo,
        string corpo,
        string url,
        string tipo)
    {
        if (string.IsNullOrWhiteSpace(idUsuario))
            return false;

        // 1. Cooldown/limite: evita criar notificações sociais repetidas.
        if (TiposSociais.Contains(tipo))
        {
            var jaEnviou = await JaEnviouRecentemente(idUsuario, tipo, COOLDOWN_SOCIAL);
            if (jaEnviou)
            {
                _logger.LogInformation(
                    "[PUSH] Cooldown ativo para {Tipo} do usuário {UsuarioId}. Notificação não criada.",
                    tipo,
                    idUsuario);
                return false;
            }
        }
        else if (TiposIncentivo.Contains(tipo) && tipo != "streak")
        {
            var total = await ContarIncentivosRecentes(idUsuario, JANELA_LIMITE_DIARIO);
            if (total >= LIMITE_DIARIO_INCENTIVO)
            {
                _logger.LogInformation(
                    "[PUSH] Limite diário de incentivos atingido para {UsuarioId}. Notificação não criada.",
                    idUsuario);
                return false;
            }
        }

        // 2. Persiste a notificação interna. Falha aqui deve ser propagada:
        // não podemos tratar falha de banco como se fosse apenas falha de push.
        var notificacao = new Notificacao
        {
            Id = Guid.NewGuid().ToString(),
            IdUsuario = idUsuario,
            Tipo = tipo,
            Mensagem = corpo,
            IdOrigem = null,
            DataCriacao = DateTime.UtcNow,
            Lida = false
        };

        await _notificacaoRepository.AddAsync(notificacao);
        LimparCacheNaoLidas(idUsuario);

        // 3. Push externo é best-effort: falha no Firebase não apaga a interna.
        try
        {
            var subs = (await _subscriptionRepository.FindAsync(s =>
                s.UserId == idUsuario && s.IsActive)).ToList();

            var tokens = subs.Select(s => s.Token).ToList();

            _logger.LogInformation(
                "[PUSH] Enviando para {UsuarioId}: tipo={Tipo}, tokens={QuantidadeTokens}",
                idUsuario,
                tipo,
                tokens.Count);

            if (tokens.Count == 0)
            {
                _logger.LogInformation(
                    "[PUSH] Nenhum token ativo para {UsuarioId}. Notificação interna criada; push não enviado.",
                    idUsuario);
                return false;
            }

            var data = new Dictionary<string, string>
            {
                { "type", tipo },
                { "url", url }
            };

            var enviouPush = await _firebaseService.SendMulticastAsync(
                tokens,
                titulo,
                corpo,
                url,
                data);

            if (enviouPush)
                _logger.LogInformation("[PUSH] Enviado com sucesso para {UsuarioId}.", idUsuario);
            else
                _logger.LogWarning(
                    "[PUSH] Falha no envio para {UsuarioId}. Notificação interna já registrada.",
                    idUsuario);

            return enviouPush;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[PUSH] Falha no envio externo para {UsuarioId}. Notificação interna já registrada.",
                idUsuario);
            return false;
        }
    }

    private async Task<bool> JaEnviouRecentemente(string idUsuario, string tipo, TimeSpan janela)
    {
        var limite = DateTime.UtcNow - janela;
        var existentes = await _notificacaoRepository.FindAsync(n =>
            n.IdUsuario == idUsuario &&
            n.Tipo == tipo &&
            n.DataCriacao > limite);
        return existentes.Any();
    }

    private async Task<int> ContarIncentivosRecentes(string idUsuario, TimeSpan janela)
    {
        var limite = DateTime.UtcNow - janela;
        var existentes = await _notificacaoRepository.FindAsync(n =>
            n.IdUsuario == idUsuario &&
            n.DataCriacao > limite &&
            TiposIncentivo.Contains(n.Tipo) &&
            n.Tipo != "streak");
        return existentes.Count();
    }

    private void LimparCacheNaoLidas(string usuarioId)
    {
        lock (_cacheLock)
        {
            _cacheNaoLidas.Remove(usuarioId);
        }
    }
}
