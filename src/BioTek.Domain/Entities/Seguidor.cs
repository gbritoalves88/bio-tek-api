using BioTek.Domain.Common;
using BioTek.Domain.Enums;

namespace BioTek.Domain.Entities;

public class Seguidor : BaseEntity
{
    public string IdUsuario { get; set; } = string.Empty;  
    public string IdSeguidor { get; set; } = string.Empty; 
}