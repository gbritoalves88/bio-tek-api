using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IComentarioLivroService
{
    Task<IEnumerable<ComentarioLivro>> GetPorLivroAsync(string livroId);
    Task<ComentarioLivro> CriarAsync(ComentarioLivro comentario);
    Task<bool> DeletarAsync(string id);
}