// BioTek.Domain/Entities/ComentarioAtividade.cs
using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class ComentarioAtividade : BaseEntity
{
    public string IdAtividade { get; set; } = string.Empty;   // _id do ComentarioLivro (atividade)
    public string IdLivro { get; set; } = string.Empty;       // redundante, mas facilita queries
    public string IdUsuario { get; set; } = string.Empty;
    public string Comentario { get; set; } = string.Empty;
    public bool Flagged { get; set; } = false;
    public double? ToxicidadeScore { get; set; }
}