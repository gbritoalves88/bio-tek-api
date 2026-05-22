using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IComunidadeService
{
    Task<List<AtividadeFeed>> GetFeedAsync(string meuId, int limite);
}