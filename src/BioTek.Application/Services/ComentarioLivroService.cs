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
        // 1. Anti-spam
        if (string.IsNullOrWhiteSpace(comentario.Comentario))
            throw new InvalidOperationException("Comentário não pode estar vazio");


        if (comentario.Comentario.Length > 200)
            throw new InvalidOperationException("Comentário passou do limite de 200 caracteres");

        // Buscar o livro para saber quem é o dono
        var livro = await _livroRepository.GetByIdAsync(comentario.IdLivro);
        if (livro == null)
            throw new InvalidOperationException("Livro não encontrado");

        // DONO do livro precisa ter avaliado
        var avaliacoesDoDono = await _avaliacaoRepository.FindAsync(
            a => a.IdLivro == comentario.IdLivro && a.IdUsuario == livro.IdUsuario);

        if (!avaliacoesDoDono.Any())
            throw new InvalidOperationException("O dono do livro ainda não avaliou. Aguarde a avaliação.");

        // 4. Salvar
        comentario.Id = Guid.NewGuid().ToString();
        comentario.DataCriacao = DateTime.UtcNow;

        await _comentarioRepository.AddAsync(comentario);

        _ = Task.Run(async () =>
        {
            var (flagged, score) = await _moderacaoService.AnalisarAsync(comentario.Comentario);
            if (flagged)
            {
                comentario.Flagged = true;
                comentario.ToxicidadeScore = score;
                await _comentarioRepository.UpdateAsync(comentario.Id, comentario);
            }
        });

        return comentario;
    }

    public async Task<bool> DeletarAsync(string id)
    {
        var existente = await _comentarioRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _comentarioRepository.DeleteAsync(id);
        return true;
    }

    public async Task<IEnumerable<ComentarioLivro>> GetPorLivroAsync(string livroId)
    {
        return await _comentarioRepository.FindAsync(c => c.IdLivro == livroId);
    }
}