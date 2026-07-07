using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class NotificacaoService : INotificacaoService
{
    private readonly IRepository<Notificacao> _notificacaoRepository;
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IRepository<Livro> _livroRepository;

    public NotificacaoService(
        IRepository<Notificacao> notificacaoRepository,
        IRepository<Usuario> usuarioRepository,
        IRepository<Livro> livroRepository)
    {
        _notificacaoRepository = notificacaoRepository;
        _usuarioRepository = usuarioRepository;
        _livroRepository = livroRepository;
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
}