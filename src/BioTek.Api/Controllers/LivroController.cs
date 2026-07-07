using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using BioTek.Api.Models;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class LivroController : ControllerBase
{
    private readonly ILivroService _livroService;
    private readonly HttpClient _httpClient;

    public LivroController(ILivroService livroService, IHttpClientFactory httpClientFactory)
    {
        _livroService = livroService;
        _httpClient = httpClientFactory.CreateClient();
    }

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    }

    /// <summary>
    /// Busca livro por ISBN na Open Library
    /// </summary>
   [HttpGet("buscar/isbn/{isbn}")]
    public async Task<IActionResult> BuscarPorIsbn(string isbn)
    {
        var livro = await _livroService.BuscarPorIsbnAsync(isbn);
        if (livro == null)
            return NotFound(new { mensagem = "Livro não encontrado" });

        return Ok(livro);
    }
    
    [HttpGet]
    public async Task<IActionResult> GetMeusLivros()
    {
        var livros = await _livroService.GetMeusLivrosAsync(GetUsuarioId());
        return Ok(livros);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPorId(string id)
    {
        var livro = await _livroService.GetPorIdAsync(id);
        if (livro == null) return NotFound();
        return Ok(livro);
    }

    /// <summary>
    /// Busca todos os livros de um usuário específico (público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("usuario/{usuarioId}")]
    public async Task<IActionResult> GetPorUsuario(string usuarioId)
    {
        var livros = await _livroService.GetPorUsuarioAsync(usuarioId);
        return Ok(livros);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] Livro livro)
    {
        livro.IdUsuario = GetUsuarioId();
        var novo = await _livroService.CriarAsync(livro);
        return CreatedAtAction(nameof(GetPorId), new { id = novo.Id }, novo);
    }



    [HttpPut("{id}")]
    public async Task<IActionResult> Atualizar(string id, [FromBody] JsonElement body)
    {
        try
        {
            // Desserializa o Livro
            var livro = JsonSerializer.Deserialize<Livro>(body.GetRawText(), 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (livro == null)
                return BadRequest(new { mensagem = "Dados inválidos" });

            // Extrai estrelas manualmente (campo extra)
            int? estrelas = null;
            if (body.TryGetProperty("estrelas", out var estrelasElement) && 
                estrelasElement.ValueKind != JsonValueKind.Null)
            {
                estrelas = estrelasElement.GetInt32();
            }

            var atualizado = await _livroService.AtualizarAsync(id, livro, estrelas);
            if (atualizado == null) 
                return NotFound(new { mensagem = "Livro não encontrado" });

            return Ok(atualizado);
        }
        catch (JsonException)
        {
            return BadRequest(new { mensagem = "JSON inválido" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deletar(string id)
    {
        var deletado = await _livroService.DeletarAsync(id);
        if (!deletado) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Busca todos os livros (feed público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("todos")]
    public async Task<IActionResult> GetAllPublico()
    {
        var livros = await _livroService.GetTodosAsync();
        return Ok(livros);
    }

    /// <summary>
    /// Biblioteca completa do usuário (livros + avaliações + comentários)
    /// </summary>
    [HttpGet("biblioteca")]
    public async Task<IActionResult> GetBibliotecaCompleta()
    {
        var livros = await _livroService.GetBibliotecaCompletaAsync(GetUsuarioId());
        return Ok(livros);
    }

    /// <summary>
    /// Atualiza progresso de leitura (página, progresso, pausar/retomar)
    /// </summary>
    [HttpPut("{id}/progresso")]
    public async Task<IActionResult> AtualizarProgresso(string id, [FromBody] AtualizarProgressoRequest request)
    {
        var atualizado = await _livroService.AtualizarProgressoAsync(id, GetUsuarioId(), request);
        if (atualizado == null)
            return NotFound(new { mensagem = "Livro não encontrado" });
        
        return Ok(atualizado);
    }
    
}