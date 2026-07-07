using BioTek.Domain.Common;
using BioTek.Domain.Enums;

namespace BioTek.Domain.Entities;
public class AtualizarProgressoRequest
{
    public int? PaginaAtual { get; set; }
    public int? TotalPaginas { get; set; }
    public int? Progresso { get; set; }
    public bool? Pausado { get; set; }
}