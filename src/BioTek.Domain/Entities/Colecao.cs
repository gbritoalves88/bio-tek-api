using BioTek.Domain.Common;
using BioTek.Domain.Enums;

namespace BioTek.Domain.Entities;


public class Colecao : BaseEntity
{
    public string IdUsuario { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string? Imagem { get; set; }
}