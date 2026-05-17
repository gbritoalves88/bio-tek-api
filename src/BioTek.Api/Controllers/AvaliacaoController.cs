using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AvaliacaoLivroController : ControllerBase
{
    private readonly IAvaliacaoLivroService _avaliacaoLivroService;

    public AvaliacaoLivroController(IAvaliacaoLivroService avaliacaoLivroService)
    {
        _avaliacaoLivroService = avaliacaoLivroService;
    }

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    }
    

    // GET: api/AvaliacaoLivro/usuario/
    [HttpGet("usuario")]
    public async Task<IActionResult> GetPorUsuario()
    {
        var idUsuario = GetUsuarioId();
        var avaliacoes = await _avaliacaoLivroService.GetPorUsuarioAsync(idUsuario);
        return Ok(avaliacoes);
    }

    // GET: api/AvaliacaoLivro/livro/{id}
    [HttpGet("livro/{id}")]
    public async Task<IActionResult> GetPorLivro(string id)
    {
        var avaliacoes = await _avaliacaoLivroService.GetPorLivroAsync(id);
        return Ok(avaliacoes);
    }

    // GET: api/AvaliacaoLivro/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var avaliacao = await _avaliacaoLivroService.GetPorIdAsync(id);
        if (avaliacao == null) return NotFound();
        return Ok(avaliacao);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AvaliacaoLivro avaliacaoLivro)
    {
        avaliacaoLivro.IdUsuario = GetUsuarioId();

          Console.WriteLine($"🧑 IdUsuario extraído do token: '{avaliacaoLivro.IdUsuario}'");
        await _avaliacaoLivroService.CriarAsync(avaliacaoLivro);
        return CreatedAtAction(nameof(GetById), new { id = avaliacaoLivro.Id }, avaliacaoLivro);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] AvaliacaoLivro avaliacaoLivro)
    {
        await _avaliacaoLivroService.AtualizarAsync(id, avaliacaoLivro);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _avaliacaoLivroService.DeletarAsync(id);
        return NoContent();
    }
    
    /// <summary>
    /// Busca todas as avaliações (feed público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("todas")]
    public async Task<IActionResult> GetAllPublico()
    {
        var avaliacoes = await _avaliacaoLivroService.GetTodasAsync();
        return Ok(avaliacoes);
    }
}