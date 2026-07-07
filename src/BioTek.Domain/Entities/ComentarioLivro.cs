using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class ComentarioLivro : BaseEntity
{
    public string IdLivro { get; set; } = string.Empty;
    public string IdUsuario { get; set; } = string.Empty;
    public string Comentario { get; set; } = string.Empty;
    public bool Flagged { get; set; } = false;
    public double? ToxicidadeScore { get; set; }
}