using System.Security.Claims;
using BioTek.Api.Models;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BioTek.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CatalogoController : ControllerBase
{
    private readonly ICatalogoService _catalogoService;
    private readonly ILivroService _livroService;

    public CatalogoController(ICatalogoService catalogoService, ILivroService livroService)
    {
        _catalogoService = catalogoService;
        _livroService = livroService;
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
    /// Lista todos os livros do catálogo
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetAll()
    {
        var catalogos = await _catalogoService.GetAllAsync();
        return Ok(catalogos);
    }

    /// <summary>
    /// Obtém um livro do catálogo por ID
    /// </summary>
    [HttpGet("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetById(string id)
    {
        var catalogo = await _catalogoService.GetByIdAsync(id);
        if (catalogo == null)
            return NotFound();
        return Ok(catalogo);
    }

    /// <summary>
    /// Busca livros no catálogo por termo
    /// </summary>
    [HttpGet("buscar")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { mensagem = "Termo de busca é obrigatório" });

        var resultados = await _catalogoService.SearchAsync(q);
        return Ok(resultados);
    }

    /// <summary>
    /// Busca livros por gênero
    /// </summary>
    [HttpGet("genero/{genero}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> SearchByGenero(string genero)
    {
        var resultados = await _catalogoService.SearchByGeneroAsync(genero);
        return Ok(resultados);
    }

    /// <summary>
    /// Busca livros recentes
    /// </summary>
    [HttpGet("recentes")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetRecentes([FromQuery] int limite = 20)
    {
        var resultados = await _catalogoService.GetMaisRecentesAsync(limite);
        return Ok(resultados);
    }

    /// <summary>
    /// Busca livro por ISBN
    /// </summary>
    [HttpGet("isbn/{isbn}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> BuscarPorIsbn(string isbn)
    {
        var catalogo = await _catalogoService.BuscarPorIsbnAsync(isbn);
        if (catalogo == null)
            return NotFound(new { mensagem = "Livro não encontrado" });

        return Ok(catalogo);
    }

    /// <summary>
    /// Busca livros externos por texto
    /// </summary>
    [HttpGet("buscar-externo")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> BuscarExterno([FromQuery] string q, [FromQuery] int limite = 20)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { mensagem = "Termo de busca é obrigatório" });

        var resultados = await _catalogoService.BuscarExternoPorTextoAsync(q, limite);
        return Ok(resultados);
    }

    // ========== IMPORTAÇÃO (APENAS ADMIN) ==========

    /// <summary>
    /// Importa livros por termo de busca (ADMIN)
    /// </summary>
    [Authorize]
    [HttpPost("importar/termo/{termo}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> ImportarPorTermo(string termo, [FromQuery] int limite = 50)
    {
        var resultado = await _catalogoService.ImportarPorTermoAsync(termo, limite);
        return Ok(resultado);
    }

    /// <summary>
    /// Importa livros por lista de ISBNs (ADMIN)
    /// </summary>
    [Authorize]
    [HttpPost("importar/isbns")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> ImportarPorIsbns([FromBody] List<string> isbns)
    {
        var resultado = await _catalogoService.ImportarPorIsbnsAsync(isbns);
        return Ok(resultado);
    }

    /// <summary>
    /// Importa livros populares por categoria (ADMIN)
    /// </summary>
    [Authorize]
    [HttpPost("importar/populares/{categoria}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> ImportarPopulares(string categoria, [FromQuery] int limite = 50)
    {
        var resultado = await _catalogoService.ImportarPopularesAsync(categoria, limite);
        return Ok(resultado);
    }

    /// <summary>
    /// Importa livros automaticamente (ADMIN)
    /// </summary>
    [Authorize]
    [HttpPost("importar/tudo")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> ImportarTudo()
    {
        var resultado = await _catalogoService.ImportarRobustoAsync("tudo", 40, 3);
        return Ok(resultado);
    }

    /// <summary>
    /// Adiciona livro do catálogo à biblioteca pessoal
    /// </summary>
    [Authorize]
    [HttpPost("{id}/adicionar-biblioteca")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> AdicionarBiblioteca(string id, [FromQuery] int? status = null)
    {
        var usuarioId = GetUsuarioId();
        
        try
        {
            var livro = await _catalogoService.AdicionarABibliotecaPessoalAsync(id, usuarioId, status);
            return Ok(livro);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    /// <summary>
    /// Importa livros em massa (ADMIN)
    /// </summary>
    [Authorize]
    [HttpPost("importar/massa")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> ImportarMassa([FromQuery] int limitePorTermo = 100)
    {
        var resultado = await _catalogoService.ImportarRobustoAsync("tudo", limitePorTermo, 1);
        return Ok(resultado);
    }

    /// <summary>
    /// Estatísticas do catálogo
    /// </summary>
    [HttpGet("estatisticas")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetEstatisticas()
    {
        var total = await _catalogoService.GetTotalCountAsync();
        var generos = await _catalogoService.GetGenerosMaisComunsAsync(10);
        
        return Ok(new
        {
            TotalLivros = total,
            GenerosMaisComuns = generos
        });
    }

    /// <summary>
    /// Importação robusta (ADMIN)
    /// </summary>
    [Authorize]
    [HttpPost("importar/robusto")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> ImportarRobusto([FromQuery] string? categoria = "tudo", [FromQuery] int livrosPorTermo = 40, [FromQuery] int paginasPorTermo = 3)
    {
        var resultado = await _catalogoService.ImportarRobustoAsync(categoria, livrosPorTermo, paginasPorTermo);
        return Ok(resultado);
    }

    /// <summary>
    /// Importa por letras (ADMIN)
    /// </summary>
    [Authorize]
    [HttpPost("importar/letras")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> ImportarPorLetras([FromQuery] int livrosPorLetra = 40, [FromQuery] int atrasoMs = 500)
    {
        var resultado = await _catalogoService.ImportarPorLetrasAsync(livrosPorLetra, atrasoMs);
        return Ok(resultado);
    }

    [HttpGet("categorias")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetCategorias()
    {
        var generos = await _catalogoService.GetGenerosMaisComunsAsync(20);
        return Ok(generos);
    }

    [Authorize]
    [HttpPost("importar/assunto/{assunto}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> ImportarPorAssunto(string assunto, [FromQuery] int limite = 200, [FromQuery] int maxPaginas = 5)
    {
        var resultado = await _catalogoService.ImportarPorAssuntoOpenLibraryAsync(assunto, limite, maxPaginas);
        return Ok(resultado);
    }

    [HttpGet("mais-adicionados")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetMaisAdicionados([FromQuery] int limite = 10)
    {
        var livros = await _catalogoService.GetMaisAdicionadosAsync(limite);
        return Ok(livros);
    }

    [Authorize]
    [HttpGet("biblioteca-inicial")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetBibliotecaInicial()
    {
        var usuarioId = GetUsuarioId();
        var dados = await _catalogoService.GetBibliotecaInicialAsync(usuarioId);
        return Ok(dados);
    }

    [Authorize]
    [HttpPost("preferencias")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> SalvarPreferencias([FromBody] PreferenciasRequest request)
    {
        var usuarioId = GetUsuarioId();
        
        try
        {
            var resultado = await _catalogoService.SalvarPreferenciasAsync(usuarioId, request.Generos);
            return Ok(new { success = true, generosPreferidos = resultado });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensagem = ex.Message });
        }
    }

    /// <summary>
    /// Busca livros por título/autor (OpenLibrary + Google Books + Biblioteca)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("buscar-por-titulo")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> BuscarPorTitulo([FromQuery] string titulo, [FromQuery] string? autor = null)
    {
        if (string.IsNullOrWhiteSpace(titulo))
            return BadRequest(new { mensagem = "Título é obrigatório" });

        if (titulo.Length > 200 || (autor != null && autor.Length > 200))
            return BadRequest(new { mensagem = "Termo de busca muito longo" });

        try
        {
            var resultados = new List<object>();
            var termo = autor != null ? $"{titulo} {autor}" : titulo;

            // ============ 1. BUSCAR NO OPENLIBRARY ============
            var openResultados = await _catalogoService.BuscarExternoPorTextoAsync(termo, 5);
            
            foreach (var livro in openResultados)
            {
                resultados.Add(new
                {
                    livro.Id,
                    livro.Titulo,
                    livro.Autor,
                    livro.Imagem,
                    livro.CodigoBarras,
                    livro.Descricao,
                    livro.Generos,
                    livro.AnoPublicacao,
                    livro.Editora,
                    livro.Idioma,
                    livro.NumeroPaginas,
                    livro.AvaliacaoMedia,
                    Fonte = livro.Fonte
                });
            }

            // ============ 2. BUSCAR NA BIBLIOTECA (apenas se não achou suficiente) ============
            if (resultados.Count < 5)
            {
                var livrosUsuarios = await _livroService.GetTodosAsync();
                var tituloLower = titulo.ToLower().Trim();
                
                foreach (var livro in livrosUsuarios)
                {
                    if (resultados.Count >= 5) break;
                    
                    // Usar reflection para acessar propriedades do objeto anônimo
                    var tipo = livro.GetType();
                    var propTitulo = tipo.GetProperty("Titulo");
                    var propAutor = tipo.GetProperty("Autor");
                    var propImagem = tipo.GetProperty("Imagem");
                    var propId = tipo.GetProperty("Id");
                    
                    var tituloLivro = propTitulo?.GetValue(livro)?.ToString();
                    
                    if (tituloLivro != null && tituloLivro.ToLower().Contains(tituloLower))
                    {
                        // Verificar se não é duplicata
                        if (!resultados.Any(r => 
                            ((dynamic)r).Titulo?.ToString().ToLower() == tituloLivro.ToLower()))
                        {
                            resultados.Add(new
                            {
                                Id = propId?.GetValue(livro)?.ToString() ?? "",
                                Titulo = tituloLivro,
                                Autor = propAutor?.GetValue(livro)?.ToString() ?? "",
                                Imagem = propImagem?.GetValue(livro)?.ToString(),
                                CodigoBarras = (string?)null,
                                Descricao = (string?)null,
                                Generos = (List<string>?)null,
                                AnoPublicacao = (int?)null,
                                Editora = (string?)null,
                                Idioma = (string?)null,
                                NumeroPaginas = (int?)null,
                                AvaliacaoMedia = (double?)null,
                                Fonte = "Biblioteca"
                            });
                        }
                    }
                }
            }

            if (resultados.Count == 0)
                return NotFound(new { mensagem = "Nenhum livro encontrado" });

            return Ok(resultados);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao buscar livro por título: {ex.Message}");
            return StatusCode(500, new { mensagem = "Erro ao buscar livro" });
        }
    }

    /// </summary>
    [HttpGet("paginado")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPaginado(
        [FromQuery] int pagina = 1, 
        [FromQuery] int tamanhoPagina = 20,
        [FromQuery] string? busca = null,
        [FromQuery] string? genero = null)
    {
        var resultado = await _catalogoService.GetPaginadoAsync(pagina, tamanhoPagina, busca, genero);
        return Ok(resultado);
    }
}