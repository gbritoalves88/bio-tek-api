using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IUsuarioService
{
    Task<IEnumerable<Usuario>> GetUsuariosAsync();
     Task<Usuario?> GetPorIdAsync(string id);
    Task<Usuario> CriarAsync(Usuario usuario);
    Task<Usuario?> GetPorNicknameAsync(string nickname);
    Task<Usuario?> AtualizarAsync(string id, Usuario usuario);
    Task<bool> DeletarAsync(string id);
    Task<object?> GetPerfilCompletoAsync(string usuarioId);
    Task<List<object>> BuscarAsync(string termo);
}