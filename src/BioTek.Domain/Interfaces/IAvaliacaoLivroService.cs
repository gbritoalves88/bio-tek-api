using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IAvaliacaoLivroService
{
    Task<IEnumerable<AvaliacaoLivro>> GetPorUsuarioAsync(string usuarioId);
    Task<IEnumerable<AvaliacaoLivro>> GetPorLivroAsync(string livroId);
    Task<AvaliacaoLivro?> GetPorIdAsync(string id);
    Task<AvaliacaoLivro> CriarAsync(AvaliacaoLivro avaliacaoLivro);
    Task<AvaliacaoLivro?> AtualizarAsync(string id, AvaliacaoLivro avaliacaoLivro);
    Task<bool> DeletarAsync(string id);
}