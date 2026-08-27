using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class StreakUsuario : BaseEntity
{
    public string IdUsuario { get; set; } = string.Empty;
    public int SequenciaAtual { get; set; }
    public int MaiorSequencia { get; set; }
    public DateTime? UltimaDataAtividade { get; set; }
}