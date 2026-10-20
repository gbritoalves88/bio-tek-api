using System.Linq.Expressions;
using BioTek.Domain.Interfaces;
using BioTek.Infrastructure.Data;
using MongoDB.Driver;

namespace BioTek.Infrastructure.Repositories;

public class MongoRepository<T> : IRepository<T> where T : class
{
    private readonly IMongoCollection<T> _collection;

    public MongoRepository(MongoDbContext context, string collectionName)
    {
        _collection = context.GetCollection<T>(collectionName);
    }

    public async Task<T?> GetByIdAsync(string id)
    {
        var filter = Builders<T>.Filter.Eq("Id", id);
        return await _collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _collection.Find(_ => true).ToListAsync();
    }

    public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> filter)
    {
        return await _collection.Find(filter).ToListAsync();
    }

    public async Task AddAsync(T entity)
    {
        await _collection.InsertOneAsync(entity);
    }

    public async Task UpdateAsync(string id, T entity)
    {
        var filter = Builders<T>.Filter.Eq("Id", id);
        await _collection.ReplaceOneAsync(filter, entity);
    }
    public async Task UpdateFieldAsync(string id, string fieldName, object value)
    {
        var filter = Builders<T>.Filter.Eq("Id", id);
        var update = Builders<T>.Update.Set(fieldName, value);
        await _collection.UpdateOneAsync(filter, update);
    }

    public async Task DeleteAsync(string id)
    {
        var filter = Builders<T>.Filter.Eq("Id", id);
        await _collection.DeleteOneAsync(filter);
    }

    
    // 🆕 Conta quantos registros batem com o filtro
    public async Task<long> CountAsync(Expression<Func<T, bool>> filter)
    {
        return await _collection.CountDocumentsAsync(filter);
    }

    // 🆕 Busca paginada (skip = quantos pular, limit = quantos trazer)
    public async Task<IEnumerable<T>> GetPaginadoAsync(
        Expression<Func<T, bool>> filter, 
        int skip, 
        int limit, 
        string? orderBy = null,
        bool descending = true)
    {
        var query = _collection.Find(filter).Skip(skip).Limit(limit);

        // 🆕 Se não passar ordenação, usa a propriedade "DataCriacao" por padrão
        var campoOrdenacao = string.IsNullOrWhiteSpace(orderBy) ? "DataCriacao" : orderBy;
        
        var sort = descending 
            ? Builders<T>.Sort.Descending(campoOrdenacao) 
            : Builders<T>.Sort.Ascending(campoOrdenacao);

        return await query.Sort(sort).ToListAsync();
    }
}

