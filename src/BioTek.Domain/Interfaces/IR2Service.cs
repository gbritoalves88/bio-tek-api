namespace BioTek.Domain.Interfaces;

public interface IR2Service
{
    /// <summary>
    /// Faz upload de uma imagem e retorna a URL pública.
    /// </summary>
    Task<string> UploadAsync(Stream stream, string extensao, string contentType);

    /// <summary>
    /// Deleta um objeto pela chave (ex: "uploads/xxx.jpg").
    /// </summary>
    Task<bool> DeleteAsync(string chave);
}