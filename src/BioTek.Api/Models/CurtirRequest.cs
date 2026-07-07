namespace BioTek.Api.Models;

public class CurtirRequest
{
    public string TipoAtividade { get; set; } = "livro";
    public string IdUsuarioDono { get; set; } = string.Empty;
    public string NomeCurtidor { get; set; } = string.Empty;
    public string TituloLivro { get; set; } = string.Empty;
}