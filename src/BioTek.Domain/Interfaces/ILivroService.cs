using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface ILivroService
{
    Task<IEnumerable<Livro>> GetPorUsuarioAsync(string usuarioId);
    Task<Livro?> GetPorIdAsync(string id);
    Task<Livro> CriarAsync(Livro livro);
    Task<Livro?> AtualizarAsync(string id, Livro livro);
    Task<bool> DeletarAsync(string id);
    Task<Livro?> BuscarPorIsbnAsync(string isbn);
}