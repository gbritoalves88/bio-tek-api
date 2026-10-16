using BioTek.Application.Settings;
using BioTek.Domain.Models;
using Microsoft.Extensions.Options;

namespace BioTek.Application.Services;

public class BookOffersService
{
    private readonly BookOffersSettings _settings;

    public BookOffersService(IOptions<BookOffersSettings> options)
    {
        _settings = options.Value;
    }

    /// <summary>
    /// Retorna as ofertas ativas, filtradas por data (se houver),
    /// ordenadas por DisplayOrder.
    /// </summary>
    public List<BookOffer> GetActiveOffers(int? limit = null)
    {
        var hoje = DateTime.UtcNow.Date;

        var ativas = _settings.Items
            .Where(o => o.IsActive)
            .Where(o => !o.StartDate.HasValue || o.StartDate.Value.Date <= hoje)
            .Where(o => !o.EndDate.HasValue || o.EndDate.Value.Date >= hoje)
            .OrderBy(o => o.DisplayOrder)
            .AsEnumerable();

        if (limit.HasValue && limit.Value > 0)
            ativas = ativas.Take(limit.Value);

        return ativas.ToList();
    }
}