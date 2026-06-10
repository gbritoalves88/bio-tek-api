namespace BioTek.Api.Models;
public class GoogleTokenResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("id_token")]
    public string IdToken { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
}