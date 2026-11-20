
namespace BioTek.Api.Models;
public class AtualizarProgressoRequest
{
    public int? PaginaAtual { get; set; }
    public int? TotalPaginas { get; set; }
    public int? Progresso { get; set; }
    public string? Cfi { get; set; }
    public bool? Pausado { get; set; }
    public int? MinutoAtual { get; set; }      // 🆕
    public int? TotalMinutos { get; set; }     // 🆕
    public string? Frase { get; set; } 
}