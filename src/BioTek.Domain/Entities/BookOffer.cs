namespace BioTek.Domain.Models;

public class BookOffer
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string AffiliateUrl { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // 🆕 Opcional — promoções sazonais
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}