using System.Security.Claims;
using BioTek.Api.Models;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTek.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CatalogoController : ControllerBase
{
    private readonly ICatalogoService _catalogoService;

    public CatalogoController(ICatalogoService catalogoService)
    {
        _catalogoService = catalogoService;
    }

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    }

    /// <summary>
    /// Lista todos os livros do catálogo
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var catalogos = await _catalogoService.GetAllAsync();
        return Ok(catalogos);
    }

    /// <summary>
    /// Obtém um livro do catálogo por ID
    /// </summary>
    [HttpGet("{id}")]
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
    public async Task<IActionResult> SearchByGenero(string genero)
    {
        var resultados = await _catalogoService.SearchByGeneroAsync(genero);
        return Ok(resultados);
    }

    /// <summary>
    /// Busca livros recentes
    /// </summary>
    [HttpGet("recentes")]
    public async Task<IActionResult> GetRecentes([FromQuery] int limite = 20)
    {
        var resultados = await _catalogoService.GetMaisRecentesAsync(limite);
        return Ok(resultados);
    }

    /// <summary>
    /// Busca livro por ISBN (busca local + APIs externas)
    /// </summary>
    [HttpGet("isbn/{isbn}")]
    public async Task<IActionResult> BuscarPorIsbn(string isbn)
    {
        var catalogo = await _catalogoService.BuscarPorIsbnAsync(isbn);
        if (catalogo == null)
            return NotFound(new { mensagem = "Livro não encontrado" });

        return Ok(catalogo);
    }

    /// <summary>
    /// Busca livros externos por texto (sem salvar no catálogo)
    /// </summary>
    [HttpGet("buscar-externo")]
    public async Task<IActionResult> BuscarExterno([FromQuery] string q, [FromQuery] int limite = 20)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { mensagem = "Termo de busca é obrigatório" });

        var resultados = await _catalogoService.BuscarExternoPorTextoAsync(q, limite);
        return Ok(resultados);
    }

    /// <summary>
    /// Importa livros por termo de busca
    /// </summary>
    [HttpPost("importar/termo/{termo}")]
    public async Task<IActionResult> ImportarPorTermo(string termo, [FromQuery] int limite = 50)
    {
        var resultado = await _catalogoService.ImportarPorTermoAsync(termo, limite);
        return Ok(resultado);
    }

    /// <summary>
    /// Importa livros por lista de ISBNs
    /// </summary>
    [HttpPost("importar/isbns")]
    public async Task<IActionResult> ImportarPorIsbns([FromBody] List<string> isbns)
    {
        var resultado = await _catalogoService.ImportarPorIsbnsAsync(isbns);
        return Ok(resultado);
    }

    /// <summary>
    /// Importa livros populares por categoria
    /// </summary>
    [HttpPost("importar/populares/{categoria}")]
    public async Task<IActionResult> ImportarPopulares(string categoria, [FromQuery] int limite = 50)
    {
        var resultado = await _catalogoService.ImportarPopularesAsync(categoria, limite);
        return Ok(resultado);
    }

    /// <summary>
    /// Importa livros automaticamente (máximo possível)
    /// </summary>
    [HttpPost("importar/tudo")]
    public async Task<IActionResult> ImportarTudo()
    {
        var termos = new List<string>
        {
            "best sellers", "classic literature", "fantasy books", "science fiction",
            "romance novels", "thriller books", "mystery books", "biography books",
            "history books", "self help books", "children books", "young adult",
            "horror books", "poetry books", "philosophy books", "psychology books",
            "business books", "cooking books", "art books", "travel books",
            "harry potter", "stephen king", "machado de assis", "jorge amado",
            "agatha christie", "george orwell", "jane austen", "charles dickens",
            "game of thrones", "lord of the rings", "the witcher", "dune"
        };
        
        var importados = 0;
        var existentes = 0;
        var erros = 0;
        var resultadosPorTermo = new List<object>();
        
        foreach (var termo in termos)
        {
            try
            {
                var resultado = await _catalogoService.ImportarPorTermoAsync(termo, 30);
                importados += resultado.Importados;
                existentes += resultado.JaExistentes;
                resultadosPorTermo.Add(new { termo, resultado.Importados, resultado.JaExistentes });
                
                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                erros++;
                resultadosPorTermo.Add(new { termo, Erro = ex.Message });
            }
        }
        
        return Ok(new
        {
            Mensagem = "Importação concluída",
            TotalTermos = termos.Count,
            LivrosImportados = importados,
            LivrosJaExistentes = existentes,
            Erros = erros,
            Detalhes = resultadosPorTermo
        });
    }

    /// <summary>
    /// Adiciona um livro do catálogo à biblioteca pessoal
    /// </summary>
    [HttpPost("{id}/adicionar-biblioteca")]
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
    /// Importa livros em massa (máximo possível)
    /// </summary>
    [HttpPost("importar/massa")]
    public async Task<IActionResult> ImportarMassa([FromQuery] int limitePorTermo = 100)
    {
        var termos = new List<string>
        {
            // Literatura brasileira
            "machado de assis", "jorge amado", "graciliano ramos", "clarice lispector",
            "guimaraes rosa", "cecilia meireles", "monteiro lobato", "carlos drummond",
            
            // Literatura internacional clássica
            "dom quixote", "guerra e paz", "crime e castigo", "cem anos solidão",
            "cemiterio praga", "o nome da rosa", "momo", "o pequeno principe",
            
            // Fantasia e Ficção
            "harry potter", "game of thrones", "o senhor dos aneis", "as cronicas de narnia",
            "a roda do tempo", "the witcher", "percy jackson", "eragon",
            
            // Suspense e Terror
            "stephen king", "agatha christie", "lovecraft", "edgar allan poe",
            "dan brown", "john grisham", "arthur conan doyle", "sherlock holmes",
            
            // Ficção Científica
            "isaac asimov", "philip k dick", "arthur c clarke", "ray bradbury",
            "orwell 1984", "fahrenheit 451", "admirável mundo novo", "fundação",
            
            // Romance
            "jane austen", "emily bronte", "nicholas sparks", "jojo moyes",
            "orgulho e preconceito", "o morro dos ventos uivantes",
            
            // Autoajuda e Desenvolvimento
            "como fazer amigos", "o poder do habito", "mindset", "habitos atomicos",
            "mais esperto que o diabo", "a sutil arte de ligar o foda-se",
            
            // Negócios e Empreendedorismo
            "pai rico pai pobre", "o segredo da mente milionaria", "empreendedorismo",
            "startup", "marketing digital", "lideranca",
            
            // Saúde e Bem-estar
            "medicina", "nutricao", "fitness", "yoga", "meditacao",
            
            // Infantojuvenil
            "turma da monica", "sítio do picapau amarelo", "diario de um banana",
            "percy jackson", "harry potter infantil",
            
            // HQs e Mangás
            "batman", "superman", "spider man", "x men", "avengers",
            "naruto", "one piece", "dragon ball", "death note",
            
            // Tecnologia
            "javascript", "python", "java", "c sharp", "angular", "react",
            "machine learning", "inteligencia artificial", "cyber security",
            
            // História e Política
            "segunda guerra mundial", "brasil colonia", "revolucao francesa",
            "guerra fria", "historia do brasil",
            
            // Religião e Espiritualidade
            "biblia", "alcorao", "bhagavad gita", "budismo", "espiritismo"
        };
        
        var todosResultados = new List<object>();
        var totalImportados = 0;
        var totalExistentes = 0;
        
        foreach (var termo in termos)
        {
            try
            {
                var resultado = await _catalogoService.ImportarPorTermoAsync(termo, limitePorTermo);
                totalImportados += resultado.Importados;
                totalExistentes += resultado.JaExistentes;
                
                todosResultados.Add(new
                {
                    Termo = termo,
                    Importados = resultado.Importados,
                    JaExistentes = resultado.JaExistentes,
                    Total = resultado.Importados + resultado.JaExistentes
                });
                
                // Aguarda 1 segundo entre requisições
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                todosResultados.Add(new { Termo = termo, Erro = ex.Message });
            }
        }
        
        var catalogoTotal = await _catalogoService.GetTotalCountAsync();
        
        return Ok(new
        {
            Mensagem = "Importação em massa concluída!",
            TermosProcessados = termos.Count,
            TotalImportadosNestaSessao = totalImportados,
            TotalJaExistentesNestaSessao = totalExistentes,
            TotalGeralNoCatalogo = catalogoTotal,
            DetalhesPorTermo = todosResultados
        });
    }

    /// <summary>
    /// Estatísticas do catálogo
    /// </summary>
    [HttpGet("estatisticas")]
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
    /// Importação robusta em massa (milhares de livros)
    /// </summary>
    [HttpPost("importar/robusto")]
    public async Task<IActionResult> ImportarRobusto([FromQuery] string? categoria = "tudo", [FromQuery] int livrosPorTermo = 40, [FromQuery] int paginasPorTermo = 3)
    {
        var resultado = await _catalogoService.ImportarRobustoAsync(categoria, livrosPorTermo, paginasPorTermo);
        return Ok(resultado);
    }

    /// <summary>
    /// Importa livros por letras do alfabeto (a-z) - Simplificado
    /// </summary>
    [HttpPost("importar/letras")]
    public async Task<IActionResult> ImportarPorLetras([FromQuery] int livrosPorLetra = 40, [FromQuery] int atrasoMs = 500)
    {
        var resultado = await _catalogoService.ImportarPorLetrasAsync(livrosPorLetra, atrasoMs);
        return Ok(resultado);
    }

    [HttpGet("categorias")]
    public async Task<IActionResult> GetCategorias()
    {
        var generos = await _catalogoService.GetGenerosMaisComunsAsync(20);
        return Ok(generos);
    }

    [HttpPost("importar/assunto/{assunto}")]
    public async Task<IActionResult> ImportarPorAssunto(string assunto, [FromQuery] int limite = 200, [FromQuery] int maxPaginas = 5)
    {
        var resultado = await _catalogoService.ImportarPorAssuntoOpenLibraryAsync(assunto, limite, maxPaginas);
        return Ok(resultado);
    }

    [HttpGet("mais-adicionados")]
    public async Task<IActionResult> GetMaisAdicionados([FromQuery] int limite = 10)
    {
        var livros = await _catalogoService.GetMaisAdicionadosAsync(limite);
        return Ok(livros);
    }


    [HttpGet("biblioteca-inicial")]
    public async Task<IActionResult> GetBibliotecaInicial()
    {
        var usuarioId = GetUsuarioId();
        var dados = await _catalogoService.GetBibliotecaInicialAsync(usuarioId);
        return Ok(dados);
    }

    [HttpPost("preferencias")]
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
}