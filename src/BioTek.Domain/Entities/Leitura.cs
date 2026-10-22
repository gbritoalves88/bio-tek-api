using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public class Leitura : BaseEntity
{
    public string IdUsuario { get; set; } = string.Empty;
    public DateTime Data { get; set; }
    public List<string> LivrosLidos { get; set; } = new();
    public int PaginasLidas { get; set; }
    public int MinutosOuvidos { get; set; }
}