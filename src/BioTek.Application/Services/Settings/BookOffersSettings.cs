using BioTek.Domain.Models;

namespace BioTek.Application.Settings;

public class BookOffersSettings
{
    public const string SectionName = "BookOffers";
    public List<BookOffer> Items { get; set; } = new();
}