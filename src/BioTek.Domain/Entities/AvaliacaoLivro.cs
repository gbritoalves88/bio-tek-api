using BioTek.Domain.Common;
using BioTek.Domain.Enums;

namespace BioTek.Domain.Entities;

public class AvaliacaoLivro : BaseEntity
{
    public string IdLivro { get; set; } = string.Empty;
    public string IdUsuario { get; set; } = string.Empty;
    public Nota Nota { get; set; }


}