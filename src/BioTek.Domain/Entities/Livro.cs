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
    public StatusLeitura Status { get; set; } = StatusLeitura.QueroLer;
    public int? Progresso { get; set; }
    public bool Pausado { get; set; } = false;
    public int? PaginaAtual { get; set; }
    public int? TotalPaginas { get; set; }
    public DateTime? UltimaLeitura { get; set; }
    
    // 🆕 Campos para Audiobook
    public string? Tipo { get; set; } = "livro";  // "livro" ou "audiobook"
    public int? MinutoAtual { get; set; }
    public int? TotalMinutos { get; set; }
}