using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface INotificacaoService
{
    Task<List<Notificacao>> GetNaoLidasAsync(string usuarioId);
    Task<List<Notificacao>> GetAllAsync(string usuarioId);
    Task MarcarComoLidasAsync(string usuarioId);
    Task CriarNotificacaoSeguidor(string idUsuario, string idSeguidor, string nomeSeguidor);
    Task CriarNotificacaoComentario(string idUsuarioDono, string idLivro, string tituloLivro, string nomeComentador);
}