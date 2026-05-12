using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using BioTek.Application.Services;
using System.Security.Cryptography.X509Certificates;
using System.Linq;

namespace BioTek.Application.Services;

public class SeguidorService : ISeguidorService
{
    private readonly IRepository<Seguidor> _seguidorRepository;
    private readonly IRepository<Usuario> _usuarioRepository;


    public SeguidorService(IRepository<Seguidor> seguidorRepository, IRepository<Usuario> usuarioRepository)
    {
        _seguidorRepository = seguidorRepository;
        _usuarioRepository = usuarioRepository;

    }

    public async Task<Seguidor> SeguirAsync(string idUsuario, string idSeguidor)
    {
        if (idUsuario == idSeguidor)
            throw new InvalidOperationException("Não pode seguir a si mesmo");

        var usuario = await _usuarioRepository.GetByIdAsync(idUsuario);
        var seguidor = await _usuarioRepository.GetByIdAsync(idSeguidor);
        if (usuario == null || seguidor == null)
            throw new InvalidOperationException("Usuário não encontrado");

        var existe = await _seguidorRepository.FindAsync(
            s => s.IdUsuario == idUsuario && s.IdSeguidor == idSeguidor);

        if (existe.Any())
            throw new InvalidOperationException("Você já segue este usuário");

        var novoSeguidor = new Seguidor
        {
            IdUsuario = idUsuario,
            IdSeguidor = idSeguidor,
            DataCriacao = DateTime.UtcNow
        };

        await _seguidorRepository.AddAsync(novoSeguidor);
        return novoSeguidor;
    }

    public async Task<bool> DeixarDeSeguirAsync(string idUsuario, string idSeguidor)
    {
        var vinculo = await _seguidorRepository.FindAsync(
            s => s.IdUsuario == idUsuario && s.IdSeguidor == idSeguidor);

        var item = vinculo.FirstOrDefault();
        if (item == null) return false;

        await _seguidorRepository.DeleteAsync(item.Id);
        return true;
    }

    public async Task<IEnumerable<Seguidor>> GetSeguidoresAsync(string idUsuario)
    {
        return await _seguidorRepository.FindAsync(s => s.IdUsuario == idUsuario);
    }

    public async Task<IEnumerable<Seguidor>> GetSeguindoAsync(string idSeguidor)
    {
        return await _seguidorRepository.FindAsync(s => s.IdSeguidor == idSeguidor);
    }

    public async Task<bool> EstaSeguindoAsync(string idUsuario, string idSeguidor)
    {
        var vinculo = await _seguidorRepository.FindAsync(
            s => s.IdUsuario == idUsuario && s.IdSeguidor == idSeguidor);
        return vinculo.Any();
    }
}