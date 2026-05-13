using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class ComentarioColecaoService : IComentarioColecaoService
{
    private readonly IRepository<ComentarioColecao> _comentarioRepository;
    private readonly IRepository<AvaliacaoColecao> _avaliacaoRepository;
    private readonly IRepository<Colecao> _colecaoRepository;

    public ComentarioColecaoService(
        IRepository<ComentarioColecao> comentarioRepository,
        IRepository<AvaliacaoColecao> avaliacaoRepository,
        IRepository<Colecao> colecaoRepository)
    {
        _comentarioRepository = comentarioRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _colecaoRepository = colecaoRepository;
    }

    public async Task<IEnumerable<ComentarioColecao>> GetPorColecaoAsync(string colecaoId)
    {
        return await _comentarioRepository.FindAsync(c => c.IdColecao == colecaoId);
    }

    public async Task<ComentarioColecao> CriarAsync(ComentarioColecao comentario)
    {
        // 1. Anti-spam
        if (string.IsNullOrWhiteSpace(comentario.Comentario))
            throw new InvalidOperationException("Comentário não pode estar vazio");

        if (comentario.Comentario.Trim().Length < 5)
            throw new InvalidOperationException("Comentário muito curto. Mínimo de 5 caracteres");

        if (comentario.Comentario.Length > 200)
            throw new InvalidOperationException("Comentário passou do limite de 200 caracteres");

        // 2. Coleção existe?
        var colecao = await _colecaoRepository.GetByIdAsync(comentario.IdColecao);
        if (colecao == null)
            throw new InvalidOperationException("Coleção não encontrada");

        // 3. Dono da coleção já avaliou?
        var avaliacoesDoDono = await _avaliacaoRepository.FindAsync(
            a => a.IdColecao == comentario.IdColecao && a.IdUsuario == colecao.IdUsuario);

        if (!avaliacoesDoDono.Any())
            throw new InvalidOperationException("O dono da coleção ainda não avaliou. Aguarde a avaliação.");

        // 4. Salvar
        comentario.Id = Guid.NewGuid().ToString();
        comentario.DataCriacao = DateTime.UtcNow;

        await _comentarioRepository.AddAsync(comentario);
        return comentario;
    }

    public async Task<bool> DeletarAsync(string id)
    {
        var existente = await _comentarioRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _comentarioRepository.DeleteAsync(id);
        return true;
    }
}