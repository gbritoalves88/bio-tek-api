using System.Linq.Expressions;

namespace BioTek.Domain.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(string id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> filter);
    Task AddAsync(T entity);
    Task UpdateAsync(string id, T entity);
    Task DeleteAsync(string id);
    Task UpdateFieldAsync(string id, string fieldName, object value);

    // 🆕 Métodos para Paginação
    Task<long> CountAsync(Expression<Func<T, bool>> filter);
    
    // 🆕 Usando string para ordenação (Domain não conhece MongoDB)
    Task<IEnumerable<T>> GetPaginadoAsync(
        Expression<Func<T, bool>> filter, 
        int skip, 
        int limit, 
        string? orderBy = null,
        bool descending = true);
}