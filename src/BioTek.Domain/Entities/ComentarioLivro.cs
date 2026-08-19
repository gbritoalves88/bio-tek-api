using BioTek.Domain.Common;

public class ComentarioLivro : BaseEntity
{
    public string IdLivro { get; set; } = string.Empty;
    public string IdUsuario { get; set; } = string.Empty;
    public string Comentario { get; set; } = string.Empty;
    public string Origem { get; set; } = "Avaliacao";
    public bool Flagged { get; set; } = false;
    public double? ToxicidadeScore { get; set; }
    
    // 🆕 Dados do progresso no momento da atividade
    public int? Progresso { get; set; }
    public int? PaginaAtual { get; set; }
    public int? TotalPaginas { get; set; }
    public int? MinutoAtual { get; set; }
    public int? TotalMinutos { get; set; }
}