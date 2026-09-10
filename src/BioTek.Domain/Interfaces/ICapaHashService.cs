// BioTek.Domain/Interfaces/ICapaHashService.cs
using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface ICapaHashService
{
    /// <summary>
    /// Popula o banco com hashes do catálogo e livros
    /// </summary>
    Task<object> PopularAsync(int? limiteCatalogo = null, int? limiteLivros = null);
    
    /// <summary>
    /// Busca livro por hash da imagem capturada
    /// </summary>
    Task<List<object>> BuscarPorHashAsync(string imagemBase64);
    
    /// <summary>
    /// Gera hash de uma imagem
    /// </summary>
    Task<long?> GerarHashAsync(string imagemBase64);  // 🆕 Mudou de ulong? para long?
    
    /// <summary>
    /// Limpa todos os hashes (para repopular)
    /// </summary>
    Task<int> LimparTodosAsync();

    Task<int> ContarPendentesAsync(string fonte);
}