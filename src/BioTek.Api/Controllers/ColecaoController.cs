using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ColecaoController : ControllerBase
{
    private readonly IColecaoService _colecaoService;

    public ColecaoController(IColecaoService colecaoService)
    {
        _colecaoService = colecaoService;
    }

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    }

    /// <summary>
    /// Lista todas as coleções
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetTodas()
    {
        var colecoes = await _colecaoService.GetColecaoAsync();
        return Ok(colecoes);
    }

    /// <summary>
    /// Busca coleção por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPorId(string id)
    {
        var colecao = await _colecaoService.GetPorIdColecaoAsync(id);
        if (colecao == null) return NotFound();
        return Ok(colecao);
    }

    /// <summary>
    /// Cria nova coleção
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] Colecao colecao)
    {
        colecao.IdUsuario = GetUsuarioId();
        var nova = await _colecaoService.CriarAsync(colecao);
        return CreatedAtAction(nameof(GetPorId), new { id = nova.Id }, nova);
    }

    /// <summary>
    /// Atualiza coleção
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Atualizar(string id, [FromBody] Colecao colecao)
    {
        var atualizada = await _colecaoService.AtualizarAsync(id, colecao);
        if (atualizada == null) return NotFound();
        return Ok(atualizada);
    }

    /// <summary>
    /// Deleta coleção
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Deletar(string id)
    {
        var result = await _colecaoService.DeletarAsync(id);
        return result ? NoContent() : NotFound();
    }
}