namespace BioTek.Api.Models;

public class RegistrarDecisaoRequest
{
    public string UserId { get; set; } = string.Empty;
    public bool Aceitou { get; set; }
}