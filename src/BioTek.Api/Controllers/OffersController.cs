using BioTek.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class OffersController : ControllerBase
{
    private readonly BookOffersService _offersService;

    public OffersController(BookOffersService offersService)
    {
        _offersService = offersService;
    }

    /// <summary>
    /// Retorna as ofertas de livros em destaque (Amazon Afiliado).
    /// Endpoint público — usado na Home antes do usuário autenticar.
    /// </summary>
    /// <param name="limit">Máximo de ofertas (default: 3, máximo: 20)</param>
    [AllowAnonymous]
    [HttpGet("books")]
    [EnableRateLimiting("GeneralPolicy")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)] // 5 min
    public IActionResult GetBookOffers([FromQuery] int limit = 3)
    {
        if (limit < 1) limit = 3;
        if (limit > 20) limit = 20;

        var offers = _offersService.GetActiveOffers(limit);

        return Ok(new
        {
            offers = offers.Select(o => new
            {
                id = o.Id,
                title = o.Title,
                author = o.Author,
                imageUrl = o.ImageUrl,
                affiliateUrl = o.AffiliateUrl,
                displayOrder = o.DisplayOrder
            }),
            updatedAt = DateTime.UtcNow
        });
    }
}