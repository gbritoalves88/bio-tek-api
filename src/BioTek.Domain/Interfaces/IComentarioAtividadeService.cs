// BioTek.Domain/Interfaces/IComentarioAtividadeService.cs
using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IComentarioAtividadeService
{
    Task<ComentarioAtividade> CriarAsync(ComentarioAtividade comentario);
    Task<bool> DeletarAsync(string id, string usuarioId);
    Task<IEnumerable<ComentarioAtividade>> GetPorAtividadeAsync(string idAtividade);
    Task<int> ContarPorAtividadeAsync(string idAtividade);
}