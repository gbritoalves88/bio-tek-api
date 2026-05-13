using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IComentarioColecaoService
{
    Task<IEnumerable<ComentarioColecao>> GetPorColecaoAsync(string colecaoId);
    Task<ComentarioColecao> CriarAsync(ComentarioColecao comentario);
    Task<bool> DeletarAsync(string id);
}