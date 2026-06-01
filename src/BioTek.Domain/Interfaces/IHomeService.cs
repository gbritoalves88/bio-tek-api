using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IHomeService
{
    Task<object> GetDashboardAsync(string usuarioId);
}