using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class Curtida : BaseEntity
{
    public string IdUsuario { get; set; } = string.Empty;
    public string IdAtividade { get; set; } = string.Empty;
    public string TipoAtividade { get; set; } = string.Empty;
}