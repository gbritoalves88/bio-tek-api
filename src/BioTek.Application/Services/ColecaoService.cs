using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class ColecaoService : IColecaoService
{
    private readonly IRepository<Colecao> _colecaoRepository;

    public ColecaoService(IRepository<Colecao> colecaoRepository)
    {
        _colecaoRepository = colecaoRepository;
    }

    public async Task<IEnumerable<Colecao>> GetColecaoAsync()
    {
        return await _colecaoRepository.GetAllAsync();
    }

    public async Task<Colecao?> GetPorIdColecaoAsync(string id)
    {
        return await _colecaoRepository.GetByIdAsync(id);
    }

    public async Task<Colecao> CriarAsync(Colecao colecao)
    {
        // Regra: nome único
        var existe = await _colecaoRepository.FindAsync(c => c.Nome == colecao.Nome);
        if (existe.Any())
            throw new InvalidOperationException("Já existe uma coleção com este nome");

        colecao.Id = Guid.NewGuid().ToString();
        colecao.DataCriacao = DateTime.UtcNow;

        await _colecaoRepository.AddAsync(colecao);
        return colecao;
    }

    public async Task<Colecao?> AtualizarAsync(string id, Colecao colecao)
    {
        var existente = await _colecaoRepository.GetByIdAsync(id);
        if (existente == null) return null;

        colecao.Id = id;
        colecao.DataCriacao = existente.DataCriacao;
        colecao.DataAtualizacao = DateTime.UtcNow;

        await _colecaoRepository.UpdateAsync(id, colecao);
        return colecao;
    }

    public async Task<bool> DeletarAsync(string id)
    {
        var existente = await _colecaoRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _colecaoRepository.DeleteAsync(id);
        return true;
    }
}