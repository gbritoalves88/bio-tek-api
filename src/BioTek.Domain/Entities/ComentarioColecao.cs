using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class ComentarioColecao : BaseEntity
{
    public string IdColecao { get; set; } = string.Empty;
    public string IdUsuario { get; set; } = string.Empty;
    public string Comentario { get; set; } = string.Empty;
}