using BioTek.Domain.Entities;

namespace BioTek.Domain.Interfaces;

public interface ICatalogoService
{
    // CRUD básico
    Task<IEnumerable<Catalogo>> GetAllAsync();
    Task<Catalogo?> GetByIdAsync(string id);
    Task<Catalogo> CreateAsync(Catalogo catalogo);
    Task<Catalogo?> UpdateAsync(string id, Catalogo catalogo);
    Task<bool> DeleteAsync(string id);
    
    // Buscas
    Task<IEnumerable<Catalogo>> SearchAsync(string termo);
    Task<IEnumerable<Catalogo>> SearchByGeneroAsync(string genero);
    Task<IEnumerable<Catalogo>> GetMaisRecentesAsync(int limite);
    
    // Integração com APIs externas
    Task<Catalogo?> BuscarPorIsbnAsync(string isbn);
    Task<List<Catalogo>> BuscarExternoPorTextoAsync(string termo, int limite = 20);
    Task<Catalogo?> BuscarExternoPorIsbnAsync(string isbn);
    
    // Importação
    Task<ImportacaoResultado> ImportarPorTermoAsync(string termo, int limite = 50);
    Task<ImportacaoResultado> ImportarPorIsbnsAsync(List<string> isbns);
    Task<ImportacaoResultado> ImportarPopularesAsync(string categoria = "best-sellers", int limite = 50);
    Task<object> ImportarRobustoAsync(string? categoria, int livrosPorTermo = 40, int paginasPorTermo = 3);
    Task<object> ImportarPorLetrasAsync(int livrosPorLetra = 40, int atrasoMs = 500);
    
    // Biblioteca pessoal
    Task<Livro?> AdicionarABibliotecaPessoalAsync(string catalogoId, string usuarioId, int? status = null);
    
    // Estatísticas
    Task<int> GetTotalCountAsync();
    Task<List<string>> GetGenerosMaisComunsAsync(int limite = 10);

    Task<ImportacaoResultado> ImportarPorAssuntoOpenLibraryAsync(string assunto, int limite, int maxPaginas);
}

public class ImportacaoResultado
{
    public int TotalProcessados { get; set; }
    public int Importados { get; set; }
    public int JaExistentes { get; set; }
    public int Erros { get; set; }
    public List<ImportacaoItem> Itens { get; set; } = new();
}

public class ImportacaoItem
{
    public string? Isbn { get; set; }
    public string? Titulo { get; set; }
    public string? Autor { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Mensagem { get; set; }
}