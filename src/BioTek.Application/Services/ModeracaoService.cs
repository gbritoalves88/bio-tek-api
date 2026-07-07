using Microsoft.Extensions.Configuration;

namespace BioTek.Application.Services;

public class ModeracaoService
{
    private readonly HashSet<string> _palavrasProibidas;

    public ModeracaoService(IConfiguration configuration)
    {
        var palavras = configuration
            .GetSection("Moderacao:PalavrasProibidas")
            .GetChildren()
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrEmpty(x))
            .ToArray();

        _palavrasProibidas = new HashSet<string>(palavras!, StringComparer.OrdinalIgnoreCase);
    }

    public Task<(bool Flagged, double Score)> AnalisarAsync(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return Task.FromResult((false, 0.0));

        var palavras = texto.Split(' ', ',', '.', '!', '?', '-', '_', ';', ':');
        
        var encontradas = palavras
            .Where(p => _palavrasProibidas.Contains(p.Trim()))
            .ToList();

        var flagged = encontradas.Count > 0;
        var score = flagged ? 1.0 : 0.0;

        return Task.FromResult((flagged, score));
    }
}