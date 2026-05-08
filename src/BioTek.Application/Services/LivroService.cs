using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;


namespace BioTek.Application.Services;

public class LivroService : ILivroService
{
    private readonly IRepository<Livro> _livroRepository;

    public LivroService(IRepository<Livro> livroRepository)
    {
        _livroRepository = livroRepository;
    }

    public async Task<IEnumerable<Livro>> GetPorUsuarioAsync(string usuarioId)
    {
        return await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId);
    }

    public async Task<Livro?> GetPorIdAsync(string id)
    {
        return await _livroRepository.GetByIdAsync(id);
    }

    public async Task<Livro> CriarAsync(Livro livro)
    {
        livro.Id = Guid.NewGuid().ToString();
        livro.DataCriacao = DateTime.UtcNow;
        livro.Status = StatusLeitura.QueroLer;
        await _livroRepository.AddAsync(livro);
        return livro;
    }

    public async Task<Livro?> AtualizarAsync(string id, Livro livro)
    {
        var existente = await _livroRepository.GetByIdAsync(id);
        if (existente == null) return null;

        livro.Id = id;
        livro.DataCriacao = existente.DataCriacao;
        livro.DataAtualizacao = DateTime.UtcNow;
        livro.IdUsuario = existente.IdUsuario; // Mantém o dono original

        await _livroRepository.UpdateAsync(id, livro);
        return livro;
    }

    public async Task<bool> DeletarAsync(string id)
    {
        var existente = await _livroRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _livroRepository.DeleteAsync(id);
        return true;
    }
}