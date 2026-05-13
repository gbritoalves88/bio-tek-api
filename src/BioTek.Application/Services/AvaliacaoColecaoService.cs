using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class AvaliacaoColecaoService : IAvaliacaoColecaoService
{
    private readonly IRepository<AvaliacaoColecao> _avaliacaoRepository;
    private readonly IRepository<Colecao> _colecaoRepository;
    private readonly IRepository<Livro> _livroRepository;

    public AvaliacaoColecaoService(
        IRepository<AvaliacaoColecao> avaliacaoRepository,
        IRepository<Colecao> colecaoRepository,
        IRepository<Livro> livroRepository)
    {
        _avaliacaoRepository = avaliacaoRepository;
        _colecaoRepository = colecaoRepository;
        _livroRepository = livroRepository;
    }

    public async Task<IEnumerable<AvaliacaoColecao>> GetPorColecaoAsync(string colecaoId)
    {
        return await _avaliacaoRepository.FindAsync(a => a.IdColecao == colecaoId);
    }

    public async Task<IEnumerable<AvaliacaoColecao>> GetPorUsuarioAsync(string usuarioId)
    {
        return await _avaliacaoRepository.FindAsync(a => a.IdUsuario == usuarioId);
    }

    public async Task<AvaliacaoColecao?> GetPorIdAsync(string id)
    {
        return await _avaliacaoRepository.GetByIdAsync(id);
    }

    public async Task<AvaliacaoColecao> CriarAsync(AvaliacaoColecao avaliacao)
    {
        // 1. Coleção existe?
        var colecao = await _colecaoRepository.GetByIdAsync(avaliacao.IdColecao);
        if (colecao == null)
            throw new InvalidOperationException("Coleção não encontrada");

        // 2. Usuário tem livros nessa coleção?
        var livrosDoUsuario = await _livroRepository.FindAsync(
            l => l.IdColecao == avaliacao.IdColecao && l.IdUsuario == avaliacao.IdUsuario);

        if (!livrosDoUsuario.Any())
            throw new InvalidOperationException("Você não possui livros nesta coleção");

        // 3. Pelo menos um livro está Lido?
        if (!livrosDoUsuario.Any(l => l.Status == StatusLeitura.Lido))
            throw new InvalidOperationException("Você precisa ter lido pelo menos um livro desta coleção");

        avaliacao.Id = Guid.NewGuid().ToString();
        avaliacao.DataCriacao = DateTime.UtcNow;

        await _avaliacaoRepository.AddAsync(avaliacao);
        return avaliacao;
    }

    public async Task<AvaliacaoColecao?> AtualizarAsync(string id, AvaliacaoColecao avaliacao)
    {
        var existente = await _avaliacaoRepository.GetByIdAsync(id);
        if (existente == null) return null;

        existente.Nota = avaliacao.Nota;
        existente.DataAtualizacao = DateTime.UtcNow;

        await _avaliacaoRepository.UpdateAsync(id, existente);
        return existente;
    }

    public async Task<bool> DeletarAsync(string id)
    {
        var existente = await _avaliacaoRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _avaliacaoRepository.DeleteAsync(id);
        return true;
    }
}