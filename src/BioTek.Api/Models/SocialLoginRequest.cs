namespace BioTek.Api.Models;

public class SocialLoginRequest
{
    public string Id { get; set; } = string.Empty;
    public string IdToken { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}