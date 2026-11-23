using BioTek.Domain.Common;

namespace BioTek.Domain.Entities;

public static class CapaHashControleStatus
{
    public const string Pendente = "Pendente";
    public const string Processado = "Processado";
    public const string SemImagem = "SemImagem";
    public const string Erro = "Erro";
}

public class CapaHashControle : BaseEntity
{
    public string IdLivro { get; set; } = string.Empty;
    public string Fonte { get; set; } = string.Empty;
    public string Status { get; set; } = CapaHashControleStatus.Pendente;
    public int Tentativas { get; set; }
    public DateTime? UltimaTentativa { get; set; }
    public DateTime? ProximaTentativa { get; set; }
    public string? UltimoErro { get; set; }
    public string? ImagemUrl { get; set; }
}
