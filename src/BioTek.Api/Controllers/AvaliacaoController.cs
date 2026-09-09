using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BioTek.Domain.Enums;  

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

    // 🆕 GetUsuarioId melhorado
    private string GetUsuarioId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                 ?? User.FindFirst("sub")?.Value;
        
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("Usuário não autenticado");
        
        return id;
    }

    // GET: api/AvaliacaoLivro/usuario/
    [HttpGet("usuario")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPorUsuario()
    {
        var idUsuario = GetUsuarioId();
        var avaliacoes = await _avaliacaoLivroService.GetPorUsuarioAsync(idUsuario);
        return Ok(avaliacoes);
    }

    // GET: api/AvaliacaoLivro/livro/{id}
    [HttpGet("livro/{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPorLivro(string id)
    {
        var avaliacoes = await _avaliacaoLivroService.GetPorLivroAsync(id);
        return Ok(avaliacoes);
    }

    // GET: api/AvaliacaoLivro/{id}
    [HttpGet("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetById(string id)
    {
        var avaliacao = await _avaliacaoLivroService.GetPorIdAsync(id);
        if (avaliacao == null) return NotFound();
        return Ok(avaliacao);
    }

    [HttpPost]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Create([FromBody] AvaliacaoLivro avaliacaoLivro)
    {
        try
        {
            avaliacaoLivro.IdUsuario = GetUsuarioId();
            
            // 🆕 Validação da nota (enum)
            if (!Enum.IsDefined(typeof(Nota), avaliacaoLivro.Nota))
                return BadRequest(new { mensagem = "Nota inválida" });
            
            var criada = await _avaliacaoLivroService.CriarAsync(avaliacaoLivro);
            return CreatedAtAction(nameof(GetById), new { id = criada.Id }, criada);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { mensagem = "Usuário não autenticado" });
        }
    }

    [HttpPut("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Update(string id, [FromBody] AvaliacaoLivro avaliacaoLivro)
    {
        try
        {
            var usuarioId = GetUsuarioId();
            
            // 🆕 Verifica se a avaliação pertence ao usuário
            var existente = await _avaliacaoLivroService.GetPorIdAsync(id);
            if (existente == null || existente.IdUsuario != usuarioId)
                return NotFound();
            
            await _avaliacaoLivroService.AtualizarAsync(id, avaliacaoLivro);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { mensagem = "Usuário não autenticado" });
        }
    }

    [HttpDelete("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Delete(string id)
    {
        var usuarioId = GetUsuarioId();
        
        // 🆕 Verifica se a avaliação pertence ao usuário
        var existente = await _avaliacaoLivroService.GetPorIdAsync(id);
        if (existente == null || existente.IdUsuario != usuarioId)
            return NotFound();
        
        await _avaliacaoLivroService.DeletarAsync(id);
        return NoContent();
    }
    
    /// <summary>
    /// Busca todas as avaliações (feed público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("todas")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetAllPublico()
    {
        var avaliacoes = await _avaliacaoLivroService.GetTodasAsync();
        return Ok(avaliacoes);
    }

    /// <summary>
    /// Busca avaliações de um usuário específico (público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("usuario/{usuarioId}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPorUsuarioPublico(string usuarioId)
    {
        var avaliacoes = await _avaliacaoLivroService.GetPorUsuarioAsync(usuarioId);
        return Ok(avaliacoes);
    }
}