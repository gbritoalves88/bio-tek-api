using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface IComunidadeService
{
    Task<List<AtividadeFeed>> GetFeedAsync(string meuId, int limite);
Task CurtirAsync(string idAtividade, string idUsuario, string tipoAtividade, string? idUsuarioDono = null, string? nomeCurtidor = null, string? tituloLivro = null);
    Task DescurtirAsync(string idAtividade, string idUsuario);
}