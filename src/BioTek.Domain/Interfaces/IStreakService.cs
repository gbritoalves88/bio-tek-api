using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IStreakService
{
    Task RegistrarAtividadeAsync(string usuarioId);
    Task<object?> GetStreakAsync(string usuarioId);
    Task<object?> GetHistoricoAsync(string usuarioId);
    Task<object?> GetMetricasMesAsync(string usuarioId); 
    Task RegistrarLeituraHojeAsync(string usuarioId, string livroId, int? paginas, int? minutos);
}