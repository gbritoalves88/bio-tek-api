using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class Notificacao : BaseEntity
{
    public string IdUsuario { get; set; } = string.Empty;  // Quem recebe
    public string Tipo { get; set; } = string.Empty;       // "seguidor" ou "comentario"
    public string Mensagem { get; set; } = string.Empty;
    public string? IdOrigem { get; set; }                  // Id do seguidor ou Id do comentário
    public bool Lida { get; set; } = false;
}