using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using System.Globalization;

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
    IRepository<Usuario> usuarioRepository,
    IRepository<ComentarioAtividade> comentarioAtividadeRepository)
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

        if (comentario.Origem == "Avaliacao" &&
            comentario.IdUsuario == livro.IdUsuario &&
            string.IsNullOrWhiteSpace(comentario.IdAtividade))
        {
            var avaliacao = avaliacoesDoDono
                .OrderByDescending(a => a.DataAtualizacao ?? a.DataCriacao)
                .FirstOrDefault();

            if (avaliacao != null)
            {
                var dataAvaliacao = avaliacao.DataAtualizacao ?? avaliacao.DataCriacao;

                var dataUtc = dataAvaliacao.Kind == DateTimeKind.Local
                    ? dataAvaliacao.ToUniversalTime()
                    : DateTime.SpecifyKind(dataAvaliacao, DateTimeKind.Utc);

                var timestamp = dataUtc.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture
                );

                comentario.IdAtividade =
                    $"{comentario.IdLivro}__livro_avaliado__{timestamp}";
            }
        }

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
            c.Origem != "Retomada" &&
            c.Origem != "Progresso");

        if (!comentarios.Any())
            return new List<object>();

        var usuariosIds = comentarios
            .Select(c => c.IdUsuario)
            .ToHashSet();

        var usuarios = await _usuarioRepository.FindAsync(
            u => usuariosIds.Contains(u.Id));

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
                ? new
                {
                    u.Id,
                    u.Nome,
                    u.Nickname,
                    u.Avatar
                }
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

    public async Task<IEnumerable<object>> GetAtividadesPorLivroAsync(string livroId)
    {
        // 1. Busca as publicações originais do livro
        var atividades = (await _comentarioRepository.FindAsync(c =>
            c.IdLivro == livroId &&
            (
                c.Origem == "Atividade" ||
                c.Origem == "Pausa" ||
                c.Origem == "Retomada"
            )
        ))
        .OrderByDescending(c => c.DataCriacao)
        .ToList();

        if (!atividades.Any())
            return new List<object>();

        // 2. Monta o IdAtividade composto usado pelas respostas
        var chavesPorAtividade = atividades.ToDictionary(
            a => a.Id,
            a =>
            {
                // DataCriacao é armazenada em UTC
                var dataUtc = DateTime.SpecifyKind(
                    a.DataCriacao,
                    DateTimeKind.Utc
                );

                var timestamp = dataUtc.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture
                );

                return $"{a.IdLivro}__livro_progresso__{timestamp}";
            }
        );

        var chaves = chavesPorAtividade.Values.ToHashSet();

        // 3. Busca as respostas na coleção correta
        var respostas = await _comentarioRepository.FindAsync(c =>
            c.IdLivro == livroId &&
            c.Origem == "Progresso" &&
            !c.Flagged &&
            c.IdAtividade != null &&
            chaves.Contains(c.IdAtividade)
        );

        // 4. Conta as respostas por publicação
        var respostasPorAtividade = respostas
            .GroupBy(c => c.IdAtividade!)
            .ToDictionary(g => g.Key, g => g.Count());

        // 5. Busca os autores das publicações
        var usuariosIds = atividades
            .Select(a => a.IdUsuario)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();

        var usuarios = await _usuarioRepository.FindAsync(
            u => usuariosIds.Contains(u.Id));

        var usuariosMap = usuarios.ToDictionary(u => u.Id);

        // 6. Retorna as publicações com suas contagens
        return atividades.Select(a =>
        {
            var idAtividade = chavesPorAtividade[a.Id];

            return (object)new
            {
                a.Id,

                // Este é o identificador que o front deve utilizar
                IdAtividade = idAtividade,

                a.IdLivro,
                a.IdUsuario,
                a.Comentario,
                a.Origem,
                a.DataCriacao,
                a.Progresso,
                a.PaginaAtual,
                a.TotalPaginas,
                a.MinutoAtual,
                a.TotalMinutos,

                TotalComentarios = respostasPorAtividade
                    .GetValueOrDefault(idAtividade, 0),

                Usuario = usuariosMap.TryGetValue(a.IdUsuario, out var u)
                    ? new
                    {
                        u.Id,
                        u.Nome,
                        u.Nickname,
                        u.Avatar
                    }
                    : null
            };
        }).ToList();
    }

    public async Task<ComentarioLivro?> GetPorIdAsync(string id)
    {
        return await _comentarioRepository.GetByIdAsync(id);
    }
}