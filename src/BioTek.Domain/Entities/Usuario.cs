using BioTek.Domain.Common;
using BioTek.Domain.Enums;

namespace BioTek.Domain.Entities;

public class Usuario : BaseEntity
{
    public string? IdGoogle { get; set; }
    public string? IdFacebook { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string? Avatar { get; set; } // ⬅️ NOVO
    public TemaUsuario Tema { get; set; } = TemaUsuario.Escuro;
    public int QuantidadeLivros { get; set; }
    public int QuantidadeLivrosLidos { get; set; }
    public double MediaAvaliacoes { get; set; }
    public bool Adm { get; set; }
    public bool Ativo { get; set; } = true;
}