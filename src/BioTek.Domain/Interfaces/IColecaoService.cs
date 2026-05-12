using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IColecaoService
{    
    Task<IEnumerable<Colecao>> GetColecaoAsync();
    Task<Colecao?> GetPorIdColecaoAsync(string colecaoId);  // ⬅️ nullable
    Task<Colecao> CriarAsync(Colecao colecao);
    Task<Colecao?> AtualizarAsync(string id, Colecao colecao); // ⬅️ nullable
    Task<bool> DeletarAsync(string id);
}