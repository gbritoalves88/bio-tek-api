// BioTek.Domain/Entities/CapaHash.cs
using System;

namespace BioTek.Domain.Entities;

public class CapaHash
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// ID do livro (Catálogo ou Livros)
    /// </summary>
    public string IdLivro { get; set; } = string.Empty;
    
    /// <summary>
    /// Fonte do hash: "Catalogo" ou "Livro"
    /// </summary>
    public string Fonte { get; set; } = string.Empty;
    
    /// <summary>
    /// Hash perceptual da capa (pHash)
    /// </summary>
    public long Hash { get; set; }
    
    /// <summary>
    /// Título do livro (cache para retorno rápido)
    /// </summary>
    public string Titulo { get; set; } = string.Empty;
    
    /// <summary>
    /// Autor do livro (cache para retorno rápido)
    /// </summary>
    public string Autor { get; set; } = string.Empty;
    
    /// <summary>
    /// URL da imagem usada para gerar o hash
    /// </summary>
    public string? ImagemUrl { get; set; }
    
    /// <summary>
    /// Data de criação do hash
    /// </summary>
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
}