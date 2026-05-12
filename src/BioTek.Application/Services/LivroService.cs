using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;


namespace BioTek.Application.Services;

public class LivroService : ILivroService
{
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<Colecao> _colecaoRepository;

    public LivroService(IRepository<Livro> livroRepository, IRepository<Colecao> colecaoRepository)
    {
        _livroRepository = livroRepository;
        _colecaoRepository = colecaoRepository;
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
        // Valida se a coleção existe (se informada)
        if (!string.IsNullOrEmpty(livro.IdColecao))
        {
            var colecao = await _colecaoRepository.GetByIdAsync(livro.IdColecao);
            if (colecao == null)
                throw new InvalidOperationException("Coleção não encontrada");
        }

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

        // Valida se a coleção existe (se informada)
        if (!string.IsNullOrEmpty(livro.IdColecao))
        {
            var colecao = await _colecaoRepository.GetByIdAsync(livro.IdColecao);
            if (colecao == null)
                throw new InvalidOperationException("Coleção não encontrada");
        }

        livro.Id = id;
        livro.DataCriacao = existente.DataCriacao;
        livro.DataAtualizacao = DateTime.UtcNow;
        livro.IdUsuario = existente.IdUsuario;

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