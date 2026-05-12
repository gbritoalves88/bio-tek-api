using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using BioTek.Application.Services;
using System.Security.Cryptography.X509Certificates;
using System.Linq;


namespace BioTek.Application.Services;

public class UsuarioService : IUsuarioService
{
    private readonly IRepository<Usuario> _usuarioRepository;


    public UsuarioService(IRepository<Usuario> usuarioRepository)
    {
        _usuarioRepository = usuarioRepository;

    }

    public async Task<bool> DeletarAsync(string id)
    {
        var existente = await _usuarioRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _usuarioRepository.DeleteAsync(id);
        return true;
    }


    public async Task<Usuario> CriarAsync(Usuario usuario)
    {
        // 🔹 Regra: Email único
        var emailExiste = await _usuarioRepository.FindAsync(u => u.Email == usuario.Email);
        if (emailExiste.Any())
            throw new InvalidOperationException("Email já cadastrado");

        // 🔹 Regra: Nickname único
        var nicknameExiste = await _usuarioRepository.FindAsync(u => u.Nickname == usuario.Nickname);
        if (nicknameExiste.Any())
            throw new InvalidOperationException("Nickname já está em uso");

        usuario.Id = Guid.NewGuid().ToString();
        usuario.DataCriacao = DateTime.UtcNow;
        usuario.Ativo = true;
        usuario.Adm = false;

        await _usuarioRepository.AddAsync(usuario);
        return usuario;
    }

    public async Task<Usuario?> AtualizarAsync(string id, Usuario usuario)
    {
        var existente = await _usuarioRepository.GetByIdAsync(id);
        if (existente == null) return null;

        usuario.Id = id;
        usuario.DataCriacao = existente.DataCriacao;

        await _usuarioRepository.UpdateAsync(id, usuario);
        return usuario;
    }

    public async Task<IEnumerable<Usuario>> GetUsuariosAsync()
    {
        var usuarios = await _usuarioRepository.GetAllAsync();
        return usuarios;
    }


    public async Task<Usuario?> GetPorIdAsync(string id)
    {
        return await _usuarioRepository.GetByIdAsync(id);
    }

    public async Task<Usuario?> GetPorNicknameAsync(string nickname)
    {
        var usuarios = await _usuarioRepository.FindAsync(u => u.Nickname == nickname);
        return usuarios.FirstOrDefault();
    }
}