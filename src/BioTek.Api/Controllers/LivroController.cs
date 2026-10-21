using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

    // 🆕 GetUsuarioId melhorado
    private string GetUsuarioId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                 ?? User.FindFirst("sub")?.Value;
        
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("Usuário não autenticado");
        
        return id;
    }

    /// <summary>
    /// Busca livro por ISBN na Open Library
    /// </summary>
    [HttpGet("buscar/isbn/{isbn}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> BuscarPorIsbn(string isbn)
    {
        var livro = await _livroService.BuscarPorIsbnAsync(isbn);
        if (livro == null)
            return NotFound(new { mensagem = "Livro não encontrado" });

        return Ok(livro);
    }
    
    [HttpGet]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetMeusLivros()
    {
        var livros = await _livroService.GetMeusLivrosAsync(GetUsuarioId());
        return Ok(livros);
    }

    [HttpGet("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPorId(string id)
    {
        var usuarioId = GetUsuarioId();
        var livro = await _livroService.GetPorIdAsync(id);
        
        // 🆕 Verifica se o livro é do usuário (ou público)
        if (livro == null) return NotFound();
        
        // 🆕 Se não é do usuário, não retorna dados completos
        if (livro.IdUsuario != usuarioId)
            return NotFound();
        
        return Ok(livro);
    }

    /// <summary>
    /// Busca todos os livros de um usuário específico (público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("usuario/{usuarioId}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPorUsuario(string usuarioId)
    {
        var livros = await _livroService.GetPorUsuarioAsync(usuarioId);
        return Ok(livros);
    }

    [HttpPost]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Criar([FromBody] Livro livro)
    {
        try
        {
            livro.IdUsuario = GetUsuarioId();
            
            // 🆕 Validações básicas
            if (string.IsNullOrWhiteSpace(livro.Titulo))
                return BadRequest(new { mensagem = "Título é obrigatório" });
            if (string.IsNullOrWhiteSpace(livro.Autor))
                return BadRequest(new { mensagem = "Autor é obrigatório" });
            
            var novo = await _livroService.CriarAsync(livro);
            return CreatedAtAction(nameof(GetPorId), new { id = novo.Id }, novo);
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
    public async Task<IActionResult> Atualizar(string id, [FromBody] JsonElement body)
    {
        try
        {
            var usuarioId = GetUsuarioId();

            // Verifica se o livro pertence ao usuário
            var existente = await _livroService.GetPorIdAsync(id);
            if (existente == null || existente.IdUsuario != usuarioId)
                return NotFound(new { mensagem = "Livro não encontrado" });

            // 🆕 Options com camelCase
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var livro = JsonSerializer.Deserialize<Livro>(body.GetRawText(), options);

            if (livro == null)
                return BadRequest(new { mensagem = "Dados inválidos" });

            // Extrai estrelas do body (não existe no Livro)
            int? estrelas = null;
            if (body.TryGetProperty("estrelas", out var estrelasElement) &&
                estrelasElement.ValueKind != JsonValueKind.Null)
            {
                estrelas = estrelasElement.GetInt32();
            }

            // 🆕 PRESERVA IdColecao do body, porque o serializer pode não mapear
            if (body.TryGetProperty("idColecao", out var idColecaoElement))
            {
                if (idColecaoElement.ValueKind == JsonValueKind.Null)
                {
                    livro.IdColecao = null;
                }
                else
                {
                    livro.IdColecao = idColecaoElement.GetString();
                }
            }

            // 🆕 Preserva Status se não veio no body
            if (!body.TryGetProperty("status", out _) || 
                body.GetProperty("status").ValueKind == JsonValueKind.Null)
            {
                livro.Status = existente.Status;
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
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { mensagem = "Usuário não autenticado" });
        }
    }

    [HttpDelete("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Deletar(string id)
    {
        var usuarioId = GetUsuarioId();
        
        // 🆕 Verifica se o livro pertence ao usuário
        var existente = await _livroService.GetPorIdAsync(id);
        if (existente == null || existente.IdUsuario != usuarioId)
            return NotFound();
        
        var deletado = await _livroService.DeletarAsync(id);
        if (!deletado) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Busca todos os livros (feed público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("todos")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetAllPublico()
    {
        var livros = await _livroService.GetTodosAsync();
        return Ok(livros);
    }

    /// <summary>
    /// Biblioteca completa do usuário
    /// </summary>
    [HttpGet("biblioteca")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetBibliotecaCompleta()
    {
        var livros = await _livroService.GetBibliotecaCompletaAsync(GetUsuarioId());
        return Ok(livros);
    }

    /// <summary>
    /// Atualiza progresso de leitura
    /// </summary>
    [HttpPut("{id}/progresso")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> AtualizarProgresso(string id, [FromBody] AtualizarProgressoRequest request)
    {
        var atualizado = await _livroService.AtualizarProgressoAsync(id, GetUsuarioId(), request);
        if (atualizado == null)
            return NotFound(new { mensagem = "Livro não encontrado" });
        
        return Ok(atualizado);
    }

    [HttpGet("proxy-imagem")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> ProxyImagem([FromQuery] string url)
    {
        if (string.IsNullOrEmpty(url))
            return BadRequest();
        
        // 🆕 Valida URL (evita SSRF)
        if (!url.StartsWith("https://covers.openlibrary.org") && 
            !url.StartsWith("http://books.google.com") &&
            !url.StartsWith("https://i.ibb.co"))
            return BadRequest(new { mensagem = "URL não permitida" });

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var bytes = await httpClient.GetByteArrayAsync(url);
            return File(bytes, "image/jpeg");
        }
        catch
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Busca atividades de leitura de um usuário (público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("usuario/{usuarioId}/atividades")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetAtividadesPorUsuario(string usuarioId)
    {
        var atividades = await _livroService.GetAtividadesPorUsuarioAsync(usuarioId);
        return Ok(atividades);
    }

    [AllowAnonymous]
    [HttpGet("{id}/detalhes-interno")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetDetalhesInterno(string id)
    {
        var detalhes = await _livroService.GetDetalhesInternoAsync(id);
        if (detalhes == null)
            return NotFound(new { mensagem = "Livro não encontrado" });
        
        return Ok(detalhes);
    }
}