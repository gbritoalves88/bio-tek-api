using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IUsuarioService
{
    Task<IEnumerable<Usuario>> GetUsuariosAsync();
     Task<Usuario?> GetPorIdAsync(string id);
    Task<Usuario> CriarAsync(Usuario avaliacaoLivro);
    Task<Usuario?> AtualizarAsync(string id, Usuario avaliacaoLivro);
    Task<bool> DeletarAsync(string id);
}