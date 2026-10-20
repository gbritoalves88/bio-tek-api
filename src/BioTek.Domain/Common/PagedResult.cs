namespace BioTek.Domain.Common;

public class PagedResult<T>
{
    public long Total { get; set; }
    public int Pagina { get; set; }
    public int TamanhoPagina { get; set; }
    public int TotalPaginas => (int)Math.Ceiling((double)Total / TamanhoPagina);
    public IEnumerable<T> Itens { get; set; } = new List<T>();
}