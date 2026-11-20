// BioTek.Application/Services/ComentarioAtividadeService.cs
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class ComentarioAtividadeService : IComentarioAtividadeService
{
    private readonly IRepository<ComentarioAtividade> _repo;
    private readonly IRepository<ComentarioLivro> _atividadeRepo;
    private readonly ModeracaoService _moderacaoService;

    public ComentarioAtividadeService(
        IRepository<ComentarioAtividade> repo,
        IRepository<ComentarioLivro> atividadeRepo,
        ModeracaoService moderacaoService)
    {
        _repo = repo;
        _atividadeRepo = atividadeRepo;
        _moderacaoService = moderacaoService;
    }

    public async Task<ComentarioAtividade> CriarAsync(ComentarioAtividade comentario)
    {
        if (string.IsNullOrWhiteSpace(comentario.Comentario))
            throw new InvalidOperationException("Comentário não pode estar vazio");

        if (comentario.Comentario.Length > 200)
            throw new InvalidOperationException("Comentário passou do limite de 200 caracteres");

        // Valida que a atividade existe (é um ComentarioLivro com Origem de atividade)
        var atividade = await _atividadeRepo.GetByIdAsync(comentario.IdAtividade);
        if (atividade == null)
            throw new InvalidOperationException("Atividade não encontrada");

        if (atividade.Origem != "Atividade" 
            && atividade.Origem != "Avaliacao" 
            && atividade.Origem != "Pausa" 
            && atividade.Origem != "Retomada")
        {
            throw new InvalidOperationException("Este registro não é uma atividade válida");
        }

        comentario.Id = Guid.NewGuid().ToString();
        comentario.DataCriacao = DateTime.UtcNow;
        comentario.IdLivro = atividade.IdLivro; // garante o vínculo com o livro
        comentario.Flagged = false;

        await _repo.AddAsync(comentario);

        // Moderação em background (igual ao ComentarioLivroService)
        _ = Task.Run(async () =>
        {
            try
            {
                var (flagged, score) = await _moderacaoService.AnalisarAsync(comentario.Comentario);
                if (flagged)
                {
                    comentario.Flagged = true;
                    comentario.ToxicidadeScore = score;
                    comentario.DataAtualizacao = DateTime.UtcNow;
                    await _repo.UpdateAsync(comentario.Id, comentario);
                }
            }
            catch { /* não afeta o comentário */ }
        });

        return comentario;
    }

    public async Task<bool> DeletarAsync(string id, string usuarioId)
    {
        var existente = await _repo.GetByIdAsync(id);
        if (existente == null || existente.IdUsuario != usuarioId) return false;
        await _repo.DeleteAsync(id);
        return true;
    }

    public async Task<IEnumerable<ComentarioAtividade>> GetPorAtividadeAsync(string idAtividade)
    {
        return await _repo.FindAsync(c => c.IdAtividade == idAtividade && !c.Flagged);
    }

    public async Task<int> ContarPorAtividadeAsync(string idAtividade)
    {
        var comentarios = await _repo.FindAsync(c => c.IdAtividade == idAtividade && !c.Flagged);
        return comentarios.Count();
    }
}