using BioTek.Domain.Common;
using BioTek.Domain.Enums;

namespace BioTek.Domain.Entities;

public class AvaliacaoColecao : BaseEntity
{
    public string IdColecao { get; set; } = string.Empty;
    public string IdUsuario { get; set; } = string.Empty;
    public Nota Nota { get; set; }
}