using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;
public class AtividadeFeed : BaseEntity
{
    public string Tipo { get; set; } = string.Empty;
    public UsuarioFeed? Usuario { get; set; }
    public LivroFeed? Livro { get; set; }
    public AvaliacaoFeed? Avaliacao { get; set; }
    public object? Comentarios { get; set; }
    public DateTime Data { get; set; }
    public int Curtidas { get; set; }        
    public bool Curtiu { get; set; } 
}

public class UsuarioFeed : BaseEntity
{
    public string Nome { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public bool Segue { get; set; } 
}

public class LivroFeed : BaseEntity
{
    public string Titulo { get; set; } = string.Empty;
    public string Autor { get; set; } = string.Empty;
    public string? Imagem { get; set; }
}

public class AvaliacaoFeed
{
    public int Nota { get; set; }
}