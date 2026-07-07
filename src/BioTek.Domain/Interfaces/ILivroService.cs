using BioTek.Domain.Entities;
using BioTek.Api.Models; 

namespace BioTek.Domain.Interfaces;

public interface ILivroService
{

    Task<List<object>> GetMeusLivrosAsync(string usuarioId);
    Task<IEnumerable<Livro>> GetPorUsuarioAsync(string usuarioId);
    Task<Livro?> GetPorIdAsync(string id);
    Task<Livro> CriarAsync(Livro livro);
    Task<Livro?> AtualizarAsync(string id, Livro livro, int? estrelas = null);
    Task<bool> DeletarAsync(string id);
    Task<Livro?> BuscarPorIsbnAsync(string isbn);
    Task<IEnumerable<Livro>> GetTodosAsync();
    Task<List<object>> GetBibliotecaCompletaAsync(string usuarioId);
    Task<Livro?> AtualizarProgressoAsync(string id, string usuarioId, AtualizarProgressoRequest request);
}