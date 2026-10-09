using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IUsuarioService
{

    Task<Usuario?> GetPorIdAsync(string id);
    Task<Usuario> CriarAsync(Usuario usuario);
    Task<Usuario?> GetPorNicknameAsync(string nickname);
    Task<Usuario?> AtualizarAsync(string id, Usuario usuario);
    Task<bool> DeletarAsync(string id);
    Task<object?> GetPerfilCompletoAsync(string usuarioId);
    Task<List<object>> BuscarAsync(string termo);
    Task UpdateFieldAsync(string id, string fieldName, object value);
    Task<IEnumerable<object>> GetUsuariosAsync();

    void LimparCachePerfil(string usuarioId);
}