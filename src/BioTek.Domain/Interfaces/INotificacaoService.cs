using BioTek.Domain.Entities;
using BioTek.Models;
using BioTek.Models.DTOs;

namespace BioTek.Domain.Interfaces;

public interface INotificacaoService
{
    Task<List<Notificacao>> GetNaoLidasAsync(string usuarioId);
    Task<List<Notificacao>> GetAllAsync(string usuarioId);
    Task MarcarComoLidasAsync(string usuarioId);
    Task CriarNotificacaoSeguidor(string idUsuario, string idSeguidor, string nomeSeguidor);
    Task CriarNotificacaoComentario(string idUsuarioDono, string idLivro, string tituloLivro, string nomeComentador);
    Task CriarNotificacaoCurtida(string idUsuarioDono, string idOrigem, string nomeCurtidor, string tipoAtividade, string tituloLivro);
     // Novos métodos para push notifications
    Task<bool> SubscribeAsync(SubscribeRequest request);
    Task<bool> UnsubscribeAsync(UnsubscribeRequest request);
    Task<List<NotificationSubscription>> GetUserSubscriptionsAsync(string userId);

    Task CriarNotificacaoIncentivo(string idUsuario, string tipo, string mensagem);
    Task CriarNotificacaoStreak(string idUsuario, int streakAtual);
    Task CriarNotificacaoLeitura(string idUsuario, string tituloLivro, int diasParado);
    Task CriarNotificacaoIncentivoInicial(string idUsuario);

}