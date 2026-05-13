using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IAvaliacaoColecaoService
{
    Task<IEnumerable<AvaliacaoColecao>> GetPorColecaoAsync(string colecaoId);
    Task<IEnumerable<AvaliacaoColecao>> GetPorUsuarioAsync(string usuarioId);
    Task<AvaliacaoColecao?> GetPorIdAsync(string id);
    Task<AvaliacaoColecao> CriarAsync(AvaliacaoColecao avaliacao);
    Task<AvaliacaoColecao?> AtualizarAsync(string id, AvaliacaoColecao avaliacao);
    Task<bool> DeletarAsync(string id);
}