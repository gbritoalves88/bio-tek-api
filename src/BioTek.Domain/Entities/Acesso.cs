using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class Acesso : BaseEntity
{
    public string IdUsuario { get; set; } = string.Empty;
}