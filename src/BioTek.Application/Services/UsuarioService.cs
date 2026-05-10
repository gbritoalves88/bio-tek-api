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
        var existentes = await _usuarioRepository.FindAsync(u => u.Email == usuario.Email);
        if (existentes.Any())
            throw new InvalidOperationException("Email já cadastrado");

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

}