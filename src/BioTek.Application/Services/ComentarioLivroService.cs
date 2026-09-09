using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class ComentarioLivroService : IComentarioLivroService
{
    private readonly IRepository<ComentarioLivro> _comentarioRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly ModeracaoService _moderacaoService;

    public ComentarioLivroService(
        IRepository<ComentarioLivro> comentarioRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Livro> livroRepository,
        ModeracaoService moderacaoService)
    {
        _comentarioRepository = comentarioRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _livroRepository = livroRepository;
        _moderacaoService = moderacaoService;
    }

    public async Task<ComentarioLivro> CriarAsync(ComentarioLivro comentario)
    {
        // 1. Validações
        if (string.IsNullOrWhiteSpace(comentario.Comentario))
            throw new InvalidOperationException("Comentário não pode estar vazio");

        if (comentario.Comentario.Length > 200)
            throw new InvalidOperationException("Comentário passou do limite de 200 caracteres");

        // 🆕 2. Busca livro + avaliação do dono em PARALELO
        var livroTask = _livroRepository.GetByIdAsync(comentario.IdLivro);
        
        var livro = await livroTask;
        if (livro == null)
            throw new InvalidOperationException("Livro não encontrado");

        // 3. Verifica se o dono já avaliou
        var avaliacoesDoDono = await _avaliacaoRepository.FindAsync(
            a => a.IdLivro == comentario.IdLivro && a.IdUsuario == livro.IdUsuario);

        if (!avaliacoesDoDono.Any())
            throw new InvalidOperationException("O dono do livro ainda não avaliou. Aguarde a avaliação.");

        // 4. Salva
        comentario.Id = Guid.NewGuid().ToString();
        comentario.DataCriacao = DateTime.UtcNow;
        comentario.Flagged = false;

        await _comentarioRepository.AddAsync(comentario);

        // 🆕 5. Moderação em background (com try-catch para não quebrar)
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
                    await _comentarioRepository.UpdateAsync(comentario.Id, comentario);
                }
            }
            catch
            {
                // Se moderação falhar, não afeta o comentário
            }
        });

        return comentario;
    }

    public async Task<bool> DeletarAsync(string id, string usuarioId)
    {
        var existente = await _comentarioRepository.GetByIdAsync(id);
        if (existente == null || existente.IdUsuario != usuarioId) 
            return false;

        await _comentarioRepository.DeleteAsync(id);
        return true;
    }

    public async Task<IEnumerable<ComentarioLivro>> GetPorLivroAsync(string livroId)
    {
        return await _comentarioRepository.FindAsync(c => c.IdLivro == livroId);
    }
}