using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class AvaliacaoLivroService : IAvaliacaoLivroService
{
    private readonly IRepository<AvaliacaoLivro> _avaliacaoLivroRepository;
    private readonly ILivroService _livroService;

    public AvaliacaoLivroService(
        IRepository<AvaliacaoLivro> avaliacaoLivroRepository, 
        ILivroService livroService)
    {
        _avaliacaoLivroRepository = avaliacaoLivroRepository;
        _livroService = livroService;
    }

    public async Task<bool> DeletarAsync(string id)
    {
        var existente = await _avaliacaoLivroRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _avaliacaoLivroRepository.DeleteAsync(id);
        return true;
    }

    public async Task<AvaliacaoLivro> CriarAsync(AvaliacaoLivro avaliacaoLivro)
    {
        // Validação da nota
        if (!Enum.IsDefined(typeof(Nota), avaliacaoLivro.Nota))
            throw new InvalidOperationException("Nota inválida");

        // Busca direta do livro
        var livro = await _livroService.GetPorIdAsync(avaliacaoLivro.IdLivro);
        
        if (livro == null)
            throw new InvalidOperationException("Livro não encontrado");
        
        if (livro.IdUsuario != avaliacaoLivro.IdUsuario)
            throw new InvalidOperationException("Livro não pertence ao usuário");

        if (livro.Status != StatusLeitura.Lido)
            throw new InvalidOperationException("Só pode avaliar livros com status Lido");

        // Verifica se já existe avaliação
        var existente = (await _avaliacaoLivroRepository.FindAsync(a => 
            a.IdLivro == avaliacaoLivro.IdLivro && 
            a.IdUsuario == avaliacaoLivro.IdUsuario)).FirstOrDefault();
        
        if (existente != null)
            throw new InvalidOperationException("Você já avaliou este livro");

        avaliacaoLivro.Id = Guid.NewGuid().ToString();
        avaliacaoLivro.DataCriacao = DateTime.UtcNow;

        await _avaliacaoLivroRepository.AddAsync(avaliacaoLivro);
        return avaliacaoLivro;
    }

    public async Task<AvaliacaoLivro?> AtualizarAsync(string id, AvaliacaoLivro avaliacaoLivro)
    {
        var existente = await _avaliacaoLivroRepository.GetByIdAsync(id);
        if (existente == null) return null;

        existente.Nota = avaliacaoLivro.Nota;
        existente.DataAtualizacao = DateTime.UtcNow;

        await _avaliacaoLivroRepository.UpdateAsync(id, existente);
        return existente;
    }

    public async Task<IEnumerable<AvaliacaoLivro>> GetPorUsuarioAsync(string usuarioIdAvaliacoes)
    {
        return await _avaliacaoLivroRepository.FindAsync(l => l.IdUsuario == usuarioIdAvaliacoes);
    }

    public async Task<IEnumerable<AvaliacaoLivro>> GetPorLivroAsync(string livroId)
    {
        return await _avaliacaoLivroRepository.FindAsync(a => a.IdLivro == livroId);
    }

    public async Task<AvaliacaoLivro?> GetPorIdAsync(string id)
    {
        return await _avaliacaoLivroRepository.GetByIdAsync(id);
    }

    public async Task<IEnumerable<AvaliacaoLivro>> GetTodasAsync()
    {
        return await _avaliacaoLivroRepository.GetAllAsync();
    }
}