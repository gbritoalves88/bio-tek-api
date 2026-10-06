using BioTek.Domain.Common;
using BioTek.Domain.Enums;

namespace BioTek.Domain.Entities;

public class Livro : BaseEntity
{
    public string IdUsuario { get; set; } = string.Empty;
    public string? IdColecao { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Autor { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string? CodigoBarras { get; set; }
    public string? Imagem { get; set; }
    public string? ArquivoLeitura { get; set; }
    public StatusLeitura Status { get; set; } = StatusLeitura.QueroLer;
    public int? Progresso { get; set; }
    public bool Pausado { get; set; } = false;
    public int? PaginaAtual { get; set; }
    public int? TotalPaginas { get; set; }
    public DateTime? UltimaLeitura { get; set; }
    
    public string? Tipo { get; set; } = "livro";
    public int? MinutoAtual { get; set; }
    public int? TotalMinutos { get; set; }

    // 🆕 Novos campos
    public List<string>? Generos { get; set; } = new();
    public int? AnoPublicacao { get; set; }
    public string? Editora { get; set; }
    public string? Idioma { get; set; }

}
public class LivroDetalhesWeb
{
    public string? Editora { get; set; }
    public int? Ano { get; set; }
    public int? Paginas { get; set; }
    public List<string>? Generos { get; set; }
    public string? Sinopse { get; set; }
}