using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IColecaoService
{
    Task<List<object>> GetColecoesAsync(string usuarioId);
    Task<Colecao?> GetPorIdAsync(string id, string usuarioId);
    Task<Colecao> CriarAsync(Colecao colecao);
    Task<Colecao?> AtualizarAsync(string id, string usuarioId, Colecao colecao);
    Task<bool> DeletarAsync(string id, string usuarioId);
}