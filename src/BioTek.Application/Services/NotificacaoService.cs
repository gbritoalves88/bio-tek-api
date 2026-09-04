using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Models;
using BioTek.Models.DTOs;
using Microsoft.Extensions.Logging;

namespace BioTek.Application.Services;

public class NotificacaoService : INotificacaoService
{
    private readonly IRepository<Notificacao> _notificacaoRepository;
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<NotificationSubscription> _subscriptionRepository;
    private readonly ILogger<NotificacaoService> _logger;

    public NotificacaoService(
        IRepository<Notificacao> notificacaoRepository,
        IRepository<Usuario> usuarioRepository,
        IRepository<Livro> livroRepository,
        IRepository<NotificationSubscription> subscriptionRepository,
        ILogger<NotificacaoService> logger)
    {
        _notificacaoRepository = notificacaoRepository;
        _usuarioRepository = usuarioRepository;
        _livroRepository = livroRepository;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
    }

    public async Task<List<Notificacao>> GetNaoLidasAsync(string usuarioId)
    {
        var notificacoes = await _notificacaoRepository.FindAsync(n => n.IdUsuario == usuarioId && !n.Lida);
        return notificacoes.OrderByDescending(n => n.DataCriacao).ToList();
    }

    public async Task<List<Notificacao>> GetAllAsync(string usuarioId)
    {
        var notificacoes = await _notificacaoRepository.FindAsync(n => n.IdUsuario == usuarioId);
        return notificacoes.OrderByDescending(n => n.DataCriacao).ToList();
    }

    public async Task MarcarComoLidasAsync(string usuarioId)
    {
        var naoLidas = await _notificacaoRepository.FindAsync(n => n.IdUsuario == usuarioId && !n.Lida);
        foreach (var n in naoLidas)
        {
            n.Lida = true;
            n.DataAtualizacao = DateTime.UtcNow;
            await _notificacaoRepository.UpdateAsync(n.Id, n);
        }
    }

   public async Task CriarNotificacaoSeguidor(string idUsuario, string idSeguidor, string nomeSeguidor)
    {

        var notificacao = new Notificacao
        {
            Id = Guid.NewGuid().ToString(),
            IdUsuario = idUsuario,
            Tipo = "seguidor",
            Mensagem = $"{nomeSeguidor} começou a seguir você",
            IdOrigem = idSeguidor,
            DataCriacao = DateTime.UtcNow,
            Lida = false
        };
        
        await _notificacaoRepository.AddAsync(notificacao);
    }

    public async Task CriarNotificacaoComentario(string idUsuarioDono, string idLivro, string tituloLivro, string nomeComentador)
    {
        var notificacao = new Notificacao
        {
            Id = Guid.NewGuid().ToString(),
            IdUsuario = idUsuarioDono,
            Tipo = "comentario",
            Mensagem = $"{nomeComentador} comentou em \"{tituloLivro}\"",
            IdOrigem = idLivro,
            DataCriacao = DateTime.UtcNow,
            Lida = false
        };
        await _notificacaoRepository.AddAsync(notificacao);
    }

    public async Task CriarNotificacaoCurtida(string idUsuarioDono, string idOrigem, string nomeCurtidor, string tipoAtividade, string tituloLivro)
    {
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
    }

     public async Task<bool> SubscribeAsync(SubscribeRequest request)
    {
        try
        {

            var oldSubscriptions = await _subscriptionRepository.FindAsync(s => 
                s.UserId == request.UserId && 
                s.Device == request.Device && 
                s.IsActive);
            
                foreach (var old in oldSubscriptions)
                {
                    old.IsActive = false;
                    await _subscriptionRepository.UpdateAsync(old.Id, old);
                }
    
            var existingSubscription = (await _subscriptionRepository.FindAsync(s => s.Token == request.Token))
                .FirstOrDefault();
            
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
        var subscriptions = await _subscriptionRepository.FindAsync(s => 
            s.UserId == userId && s.IsActive);
        
        return subscriptions.ToList();
    }

    // 🆕 Notificação de incentivo (streak, leitura, incentivo)
    public async Task CriarNotificacaoIncentivo(string idUsuario, string tipo, string mensagem)
    {
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
    }

    // 🆕 Notificação de streak
    public async Task CriarNotificacaoStreak(string idUsuario, int streakAtual)
    {
        await CriarNotificacaoIncentivo(
            idUsuario,
            "streak",
            $"🔥 Sua sequência está em {streakAtual} dias! Continue lendo hoje!"
        );
    }

    // 🆕 Notificação de leitura parada
    public async Task CriarNotificacaoLeitura(string idUsuario, string tituloLivro, int diasParado)
    {
        await CriarNotificacaoIncentivo(
            idUsuario,
            "leitura",
            $"📚 Você está há {diasParado} dias sem ler '{tituloLivro}'. Continue de onde parou!"
        );
    }

    // 🆕 Notificação de incentivo inicial
    public async Task CriarNotificacaoIncentivoInicial(string idUsuario)
    {
        await CriarNotificacaoIncentivo(
            idUsuario,
            "incentivo",
            "📕 Adicione seu primeiro livro e comece sua jornada literária!"
        );
    }

    
}