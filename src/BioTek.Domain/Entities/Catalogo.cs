using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class Catalogo : BaseEntity
{
    public string Titulo { get; set; } = string.Empty;
    public string Autor { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string? Imagem { get; set; }
    public string? CodigoBarras { get; set; } 
    public string? Fonte { get; set; }  
    public string? FonteId { get; set; }  
    public List<string> Generos { get; set; } = new();
    public int? AnoPublicacao { get; set; }
    public string? Editora { get; set; }
    public int? NumeroPaginas { get; set; }
    public double? AvaliacaoMedia { get; set; } 
     public string? Idioma { get; set; }  
}