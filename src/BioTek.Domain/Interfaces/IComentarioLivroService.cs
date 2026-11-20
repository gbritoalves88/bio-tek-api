using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IComentarioLivroService
{
    Task<ComentarioLivro> CriarAsync(ComentarioLivro comentario);
    Task<bool> DeletarAsync(string id, string usuarioId);
    Task<IEnumerable<object>> GetPorLivroAsync(string livroId);
    Task<IEnumerable<object>> GetPorAtividadeAsync(string idAtividade); // 🆕
    Task<ComentarioLivro?> GetPorIdAsync(string id); // 🆕
}