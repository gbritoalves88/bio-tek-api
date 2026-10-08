using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

    // 🆕 GetUsuarioId padronizado
    private string GetUsuarioId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("Usuário não autenticado");

        return id;
    }

    /// <summary>
    /// Lista as prateleiras do usuário logado (com 3 capas cada)
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetMinhasPrateleiras()
    {
        var colecoes = await _colecaoService.GetColecoesAsync(GetUsuarioId());
        return Ok(colecoes);
    }

    /// <summary>
    /// Busca prateleira por ID (só do próprio usuário)
    /// </summary>
    [HttpGet("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPorId(string id)
    {
        var usuarioId = GetUsuarioId();
        var colecao = await _colecaoService.GetPorIdAsync(id, usuarioId);

        if (colecao == null) return NotFound();
        return Ok(colecao);
    }

    /// <summary>
    /// Cria nova prateleira
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Criar([FromBody] Colecao colecao)
    {
        try
        {
            colecao.IdUsuario = GetUsuarioId();
            var nova = await _colecaoService.CriarAsync(colecao);
            return CreatedAtAction(nameof(GetPorId), new { id = nova.Id }, nova);
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

    /// <summary>
    /// Atualiza prateleira
    /// </summary>
    [HttpPut("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Atualizar(string id, [FromBody] Colecao colecao)
    {
        try
        {
            var usuarioId = GetUsuarioId();
            var atualizada = await _colecaoService.AtualizarAsync(id, usuarioId, colecao);

            if (atualizada == null)
                return NotFound(new { mensagem = "Prateleira não encontrada" });

            return Ok(atualizada);
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

    /// <summary>
    /// Deleta prateleira (livros não são deletados, só desvinculados)
    /// </summary>
    [HttpDelete("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Deletar(string id)
    {
        try
        {
            var usuarioId = GetUsuarioId();
            var deletado = await _colecaoService.DeletarAsync(id, usuarioId);

            if (!deletado) return NotFound();
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { mensagem = "Usuário não autenticado" });
        }
    }
}