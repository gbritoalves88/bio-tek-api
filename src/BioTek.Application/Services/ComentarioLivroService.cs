using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class ComentarioLivroService : IComentarioLivroService
{
    private readonly IRepository<ComentarioLivro> _comentarioRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly ModeracaoService _moderacaoService;

    private readonly IRepository<Usuario> _usuarioRepository;

    public ComentarioLivroService(
        IRepository<ComentarioLivro> comentarioRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<Livro> livroRepository,
        ModeracaoService moderacaoService,
        IRepository<Usuario> usuarioRepository)
    {
        _comentarioRepository = comentarioRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _livroRepository = livroRepository;
        _moderacaoService = moderacaoService;
        _usuarioRepository = usuarioRepository;
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

        // Se é privado e não é do usuário, bloqueia
        if (livro.Publico == false && livro.IdUsuario != comentario.IdUsuario)
            throw new InvalidOperationException("Livro não está disponível para comentários");

        // 3. Verifica se o dono já avaliou
        var avaliacoesDoDono = await _avaliacaoRepository.FindAsync(
            a => a.IdLivro == comentario.IdLivro && a.IdUsuario == livro.IdUsuario);

        //if (!avaliacoesDoDono.Any())
         //   throw new InvalidOperationException("O dono do livro ainda não avaliou. Aguarde a avaliação.");

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

    public async Task<IEnumerable<object>> GetPorLivroAsync(string livroId)
    {
        var comentarios = await _comentarioRepository.FindAsync(c =>
            c.IdLivro == livroId &&
            c.Origem != "Atividade" &&
            c.Origem != "Pausa" &&
            c.Origem != "Retomada");

        if (!comentarios.Any()) return new List<object>();

        var usuariosIds = comentarios.Select(c => c.IdUsuario).ToHashSet();
        var usuarios = await _usuarioRepository.FindAsync(u => usuariosIds.Contains(u.Id));
        var usuariosMap = usuarios.ToDictionary(u => u.Id);

        return comentarios.Select(c => (object)new
        {
            c.Id,
            c.IdUsuario,
            c.IdLivro,
            c.Comentario,
            c.Origem,
            c.IdAtividade,
            c.DataCriacao,
            Usuario = usuariosMap.TryGetValue(c.IdUsuario, out var u)
                ? new { u.Id, u.Nome, u.Nickname, u.Avatar }
                : null
        });
    }

    public async Task<IEnumerable<object>> GetPorAtividadeAsync(string idAtividade)
    {
        var comentarios = await _comentarioRepository.FindAsync(c => 
            c.IdAtividade == idAtividade && !c.Flagged);
        
        if (!comentarios.Any()) return new List<object>();

        // Busca todos os usuários de uma vez (evita N+1)
        var usuariosIds = comentarios.Select(c => c.IdUsuario).ToHashSet();
        var usuarios = await _usuarioRepository.FindAsync(u => usuariosIds.Contains(u.Id));
        var usuariosMap = usuarios.ToDictionary(u => u.Id);

        return comentarios.Select(c => (object)new
        {
            c.Id,
            c.IdUsuario,
            c.Comentario,
            c.DataCriacao,
            c.IdAtividade,
            c.Origem,
            Usuario = usuariosMap.TryGetValue(c.IdUsuario, out var u) 
                ? new { u.Id, u.Nome, u.Nickname, u.Avatar } 
                : null
        }).OrderBy(c => ((dynamic)c).DataCriacao);
    }

    public async Task<ComentarioLivro?> GetPorIdAsync(string id)
    {
        return await _comentarioRepository.GetByIdAsync(id);
    }
}