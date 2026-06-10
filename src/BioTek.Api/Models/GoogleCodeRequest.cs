
namespace BioTek.Api.Models;

public class GoogleCodeRequest
{
    public string Code { get; set; } = string.Empty;
    public string? RedirectUri { get; set; }
}