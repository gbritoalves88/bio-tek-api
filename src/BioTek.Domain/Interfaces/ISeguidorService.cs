using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface ISeguidorService
{
    Task<Seguidor> SeguirAsync(string idUsuario, string idSeguidor);
    Task<bool> DeixarDeSeguirAsync(string idUsuario, string idSeguidor);
    Task<IEnumerable<Seguidor>> GetSeguidoresAsync(string idUsuario);
    Task<IEnumerable<Seguidor>> GetSeguindoAsync(string idSeguidor);
    Task<bool> EstaSeguindoAsync(string idUsuario, string idSeguidor);

}