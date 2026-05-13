using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AvaliacaoColecaoController : ControllerBase
{
    private readonly IAvaliacaoColecaoService _avaliacaoColecaoService;

    public AvaliacaoColecaoController(IAvaliacaoColecaoService avaliacaoColecaoService)
    {
        _avaliacaoColecaoService = avaliacaoColecaoService;
    }

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value!;
    }

    /// <summary>
    /// Avaliações de uma coleção
    /// </summary>
    [HttpGet("colecao/{colecaoId}")]
    public async Task<IActionResult> GetPorColecao(string colecaoId)
    {
        var avaliacoes = await _avaliacaoColecaoService.GetPorColecaoAsync(colecaoId);
        return Ok(avaliacoes);
    }

    /// <summary>
    /// Avaliações do usuário logado
    /// </summary>
    [HttpGet("usuario")]
    public async Task<IActionResult> GetPorUsuario()
    {
        var avaliacoes = await _avaliacaoColecaoService.GetPorUsuarioAsync(GetUsuarioId());
        return Ok(avaliacoes);
    }

    /// <summary>
    /// Busca avaliação por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPorId(string id)
    {
        var avaliacao = await _avaliacaoColecaoService.GetPorIdAsync(id);
        if (avaliacao == null) return NotFound();
        return Ok(avaliacao);
    }

    /// <summary>
    /// Cria avaliação de coleção
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] AvaliacaoColecao avaliacao)
    {
        avaliacao.IdUsuario = GetUsuarioId();
        var nova = await _avaliacaoColecaoService.CriarAsync(avaliacao);
        return CreatedAtAction(nameof(GetPorId), new { id = nova.Id }, nova);
    }

    /// <summary>
    /// Atualiza nota da avaliação
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Atualizar(string id, [FromBody] AvaliacaoColecao avaliacao)
    {
        var atualizada = await _avaliacaoColecaoService.AtualizarAsync(id, avaliacao);
        if (atualizada == null) return NotFound();
        return Ok(atualizada);
    }

    /// <summary>
    /// Remove avaliação
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Deletar(string id)
    {
        var result = await _avaliacaoColecaoService.DeletarAsync(id);
        return result ? NoContent() : NotFound();
    }
}