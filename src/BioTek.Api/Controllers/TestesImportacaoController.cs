using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BioTek.Api.Controllers;

/// <summary>
/// Endpoints temporários para diagnóstico manual de importação e geração de CapaHashes.
/// Remover após concluir os testes.
/// </summary>
[ApiController]
[Route("api/testes-importacao")]
[AllowAnonymous]
public class TestesImportacaoController : ControllerBase
{
    private readonly ICatalogoService _catalogoService;
    private readonly ICapaHashService _capaHashService;
    private readonly ILogger<TestesImportacaoController> _logger;

    public TestesImportacaoController(
        ICatalogoService catalogoService,
        ICapaHashService capaHashService,
        ILogger<TestesImportacaoController> logger)
    {
        _catalogoService = catalogoService;
        _capaHashService = capaHashService;
        _logger = logger;
    }

    /// <summary>
    /// Testa a importação do Google Books. Use uma categoria/termo pequeno nos testes.
    /// Ex.: POST /api/testes-importacao/google?categoria=brasileiros&livrosPorTermo=5&paginasPorTermo=1
    /// </summary>
    [HttpPost("google")]
    [EnableRateLimiting("AdminPolicy")]
    public async Task<IActionResult> ImportarGoogle(
        [FromQuery] string categoria = "brasileiros",
        [FromQuery] int livrosPorTermo = 5,
        [FromQuery] int paginasPorTermo = 1)
    {
        if (string.IsNullOrWhiteSpace(categoria) || categoria.Length > 80)
            return BadRequest(new { mensagem = "Informe uma categoria/termo válido (até 80 caracteres)." });

        if (livrosPorTermo is < 1 or > 10 || paginasPorTermo is < 1 or > 2)
            return BadRequest(new { mensagem = "Para teste, livrosPorTermo deve ser 1-10 e paginasPorTermo 1-2." });

        try
        {
            _logger.LogInformation("Teste manual Google Books iniciado. Categoria={Categoria}", categoria);
            var resultado = await _catalogoService.ImportarRobustoAsync(categoria, livrosPorTermo, paginasPorTermo);
            _logger.LogInformation("Teste manual Google Books concluído: {Resultado}", resultado);
            return Ok(new { fonte = "GoogleBooks", resultado });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no teste manual Google Books");
            return StatusCode(500, new { mensagem = ex.Message });
        }
    }

    /// <summary>
    /// Testa a importação de um assunto da Open Library.
    /// Ex.: POST /api/testes-importacao/openlibrary/brazilian_literature?limite=10&maxPaginas=1
    /// </summary>
    [HttpPost("openlibrary/{assunto}")]
    [EnableRateLimiting("AdminPolicy")]
    public async Task<IActionResult> ImportarOpenLibrary(
        string assunto,
        [FromQuery] int limite = 10,
        [FromQuery] int maxPaginas = 1)
    {
        if (string.IsNullOrWhiteSpace(assunto) || assunto.Length > 80)
            return BadRequest(new { mensagem = "Informe um assunto válido (até 80 caracteres)." });

        if (limite is < 1 or > 50 || maxPaginas is < 1 or > 2)
            return BadRequest(new { mensagem = "Para teste, limite deve ser 1-50 e maxPaginas 1-2." });

        try
        {
            _logger.LogInformation("Teste manual OpenLibrary iniciado. Assunto={Assunto}", assunto);
            var resultado = await _catalogoService.ImportarPorAssuntoOpenLibraryAsync(assunto, limite, maxPaginas);
            _logger.LogInformation("Teste manual OpenLibrary concluído: {Resultado}", resultado);
            return Ok(new { fonte = "OpenLibrary", assunto, resultado });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no teste manual OpenLibrary: {Assunto}", assunto);
            return StatusCode(500, new { mensagem = ex.Message });
        }
    }

    /// <summary>
    /// Gera hashes de uma única fonte por chamada, com limite pequeno para diagnóstico.
    /// Ex.: POST /api/testes-importacao/hashes?fonte=Catalogo&limite=5
    /// </summary>
    [HttpPost("hashes")]
    [EnableRateLimiting("AdminPolicy")]
    public async Task<IActionResult> GerarHashes(
        [FromQuery] string fonte = "Catalogo",
        [FromQuery] int limite = 5)
    {
        if (!string.Equals(fonte, "Catalogo", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fonte, "Livro", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensagem = "Fonte deve ser Catalogo ou Livro." });

        if (limite is < 1 or > 10)
            return BadRequest(new { mensagem = "Para teste, limite deve ser entre 1 e 10." });

        fonte = fonte.Equals("Livro", StringComparison.OrdinalIgnoreCase) ? "Livro" : "Catalogo";

        try
        {
            var pendentesAntes = await _capaHashService.ContarPendentesAsync(fonte);
            _logger.LogInformation("Teste manual de CapaHash iniciado. Fonte={Fonte}, Limite={Limite}, Pendentes={Pendentes}",
                fonte, limite, pendentesAntes);

            var resultado = fonte == "Catalogo"
                ? await _capaHashService.PopularAsync(limite, 0)
                : await _capaHashService.PopularAsync(0, limite);

            var pendentesDepois = await _capaHashService.ContarPendentesAsync(fonte);
            _logger.LogInformation("Teste manual de CapaHash concluído. Fonte={Fonte}, PendentesAntes={Antes}, PendentesDepois={Depois}, Resultado={Resultado}",
                fonte, pendentesAntes, pendentesDepois, resultado);

            return Ok(new
            {
                fonte,
                limiteSolicitado = limite,
                pendentesAntes,
                pendentesDepois,
                hashesGerados = Math.Max(0, pendentesAntes - pendentesDepois),
                resultado
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no teste manual de CapaHash. Fonte={Fonte}", fonte);
            return StatusCode(500, new { mensagem = ex.Message });
        }
    }
}
