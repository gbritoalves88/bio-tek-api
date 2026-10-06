using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class UsuarioService : IUsuarioService
{
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;
    private readonly IRepository<ComentarioLivro> _comentarioRepository;

    // 🆕 Cache do perfil completo
    private static readonly Dictionary<string, (object Data, DateTime Time)> _cachePerfil = new();
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromSeconds(30);

    public UsuarioService(
        IRepository<Usuario> usuarioRepository,
        IRepository<Livro> livroRepository,
        IRepository<AvaliacaoLivro> avaliacaoRepository,
        IRepository<ComentarioLivro> comentarioRepository)
    {
        _usuarioRepository = usuarioRepository;
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
        _comentarioRepository = comentarioRepository;
    }

    public async Task<bool> DeletarAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("ID é obrigatório");

        var existente = await _usuarioRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _usuarioRepository.DeleteAsync(id);

        // 🆕 Limpar cache do perfil
        LimparCachePerfil(id);

        return true;
    }

    public async Task<Usuario> CriarAsync(Usuario usuario)
    {
        // 🆕 Validação de entrada
        if (usuario == null)
            throw new ArgumentException("Usuário é obrigatório");

        if (string.IsNullOrWhiteSpace(usuario.Email))
            throw new InvalidOperationException("Email é obrigatório");

        if (string.IsNullOrWhiteSpace(usuario.Nickname))
            throw new InvalidOperationException("Nickname é obrigatório");

        // 🔹 Regra: Email único
        var emailExiste = await _usuarioRepository.FindAsync(u => u.Email == usuario.Email);
        if (emailExiste.Any())
            throw new InvalidOperationException("Email já cadastrado");

        // 🔹 Regra: Nickname único
        var nicknameExiste = await _usuarioRepository.FindAsync(u => u.Nickname == usuario.Nickname);
        if (nicknameExiste.Any())
            throw new InvalidOperationException("Nickname já está em uso");

        usuario.Id = Guid.NewGuid().ToString();
        usuario.DataCriacao = DateTime.UtcNow;
        usuario.Ativo = true;
        usuario.Adm = false; // 🆕 Força Adm=false no cadastro
        usuario.AceitouTermos = false; // 🆕 Força termos não aceitos
        usuario.DataAceiteTermos = null;

        await _usuarioRepository.AddAsync(usuario);
        return usuario;
    }

    public async Task<Usuario?> AtualizarAsync(string id, Usuario usuario)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("ID é obrigatório");

        if (usuario == null)
            throw new ArgumentException("Usuário é obrigatório");

        var existente = await _usuarioRepository.GetByIdAsync(id);
        if (existente == null) return null;

        // Preserva dados que não podem ser alterados
        usuario.Email = existente.Email; // 🆕 Email não pode ser alterado
        usuario.Adm = existente.Adm; // 🆕 Adm não pode ser alterado
        usuario.DataCriacao = existente.DataCriacao;
        usuario.IdGoogle = existente.IdGoogle ?? usuario.IdGoogle;
        usuario.IdFacebook = existente.IdFacebook ?? usuario.IdFacebook;

        // 🆕 Atualiza campos editáveis
        existente.Nome = usuario.Nome;
        existente.Nickname = usuario.Nickname;
        existente.Descricao = usuario.Descricao;
        existente.Avatar = usuario.Avatar;
        existente.Tema = usuario.Tema;
        existente.Ativo = usuario.Ativo;

        // 🆕 Só atualiza AceitouTermos se for true (nunca volta para false)
        if (usuario.AceitouTermos)
        {
            existente.AceitouTermos = true;
            existente.DataAceiteTermos = usuario.DataAceiteTermos ?? DateTime.UtcNow;
        }

        existente.DataAtualizacao = DateTime.UtcNow;

        await _usuarioRepository.UpdateAsync(id, existente);

        // 🆕 Limpar cache do perfil
        LimparCachePerfil(id);

        return existente;
    }

    public async Task<IEnumerable<object>> GetUsuariosAsync()
    {
        var usuarios = await _usuarioRepository.GetAllAsync();
        
        // 🆕 Não retornar dados sensíveis
        return usuarios.Select(u => new
        {
            u.Id,
            u.Nome,
            u.Nickname,
            u.Avatar,
            u.Descricao,
            u.Ativo
        });
    }

    public async Task<Usuario?> GetPorIdAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        return await _usuarioRepository.GetByIdAsync(id);
    }

    public async Task<Usuario?> GetPorNicknameAsync(string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            return null;

        var usuarios = await _usuarioRepository.FindAsync(u => u.Nickname == nickname);
        return usuarios.FirstOrDefault();
    }

    public async Task<object?> GetPerfilCompletoAsync(string usuarioId)
    {
        if (string.IsNullOrEmpty(usuarioId))
            return null;

        // 🆕 Verificar cache
        lock (_cacheLock)
        {
            if (_cachePerfil.TryGetValue(usuarioId, out var cached) && 
                DateTime.UtcNow - cached.Time < CACHE_DURATION)
            {
                return cached.Data;
            }
        }

        var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario == null) return null;

        // ============ BUSCAR DADOS RELACIONADOS EM PARALELO ============
        var tarefas = new
        {
            Livros = _livroRepository.FindAsync(l => l.IdUsuario == usuarioId),
            Avaliacoes = _avaliacaoRepository.FindAsync(a => a.IdUsuario == usuarioId)
        };

        await Task.WhenAll(tarefas.Livros, tarefas.Avaliacoes);

        var livros = await tarefas.Livros;
        var avaliacoes = await tarefas.Avaliacoes;

        // 🆕 Buscar comentários apenas dos livros do usuário
        var idsLivros = livros.Select(l => l.Id).ToHashSet();
        var comentarios = idsLivros.Any()
            ? await _comentarioRepository.FindAsync(c => idsLivros.Contains(c.IdLivro))
            : new List<ComentarioLivro>();

        // 🆕 Buscar usuários apenas dos comentários
        var idsUsuarios = comentarios.Select(c => c.IdUsuario).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();
        var usuariosComentarios = idsUsuarios.Any()
            ? await _usuarioRepository.FindAsync(u => idsUsuarios.Contains(u.Id))
            : new List<Usuario>();

        var usuarioMap = usuariosComentarios.ToDictionary(u => u.Id);

        // 🆕 Agrupar dados para evitar N+1
        var avaliacoesPorLivro = avaliacoes
            .GroupBy(a => a.IdLivro)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault());

        var comentariosPorLivro = comentarios
            .GroupBy(c => c.IdLivro)
            .ToDictionary(g => g.Key, g => g.ToList());

        var resultado = new
        {
            usuario.Id,
            usuario.IdGoogle,
            usuario.Nome,
            usuario.Nickname,
            usuario.Email, // 🆕 Avaliar se deve expor email
            usuario.Descricao,
            usuario.Avatar,
            usuario.Tema,
            usuario.Adm,
            usuario.Ativo,
            usuario.DataCriacao,
            usuario.DataAtualizacao,
            usuario.AceitouTermos,
            usuario.DataAceiteTermos,
            QuantidadeLivros = livros.Count(),
            QuantidadeLivrosLidos = livros.Count(l => l.Status == StatusLeitura.Lido),
            MediaAvaliacoes = avaliacoes.Any()
                ? Math.Round(avaliacoes.Average(a => (int)a.Nota), 1)
                : 0,
            Livros = livros.Select(l =>
            {
                var avaliacao = avaliacoesPorLivro.GetValueOrDefault(l.Id);
                var comentariosDoLivro = comentariosPorLivro.GetValueOrDefault(l.Id, new List<ComentarioLivro>());

                return new
                {
                    l.Id,
                    l.Titulo,
                    l.Autor,
                    l.Imagem,
                    l.Status,
                    l.DataCriacao,
                    l.Descricao,
                    l.Tipo,
                    l.TotalMinutos,
                    l.MinutoAtual,
                    l.TotalPaginas,
                    l.PaginaAtual,
                    l.Generos,
                    l.AnoPublicacao,
                    l.Editora,
                    l.Idioma,
                    l.ArquivoLeitura,
                    Avaliacao = avaliacao != null
                        ? new { avaliacao.Id, avaliacao.Nota, avaliacao.DataCriacao }
                        : null,
                    Comentarios = comentariosDoLivro.Select(c =>
                    {
                        var u = c.IdUsuario != null ? usuarioMap.GetValueOrDefault(c.IdUsuario) : null;
                        return new
                        {
                            c.Id,
                            c.Comentario,
                            c.DataCriacao,
                            Usuario = u != null
                                ? new { u.Id, u.Nickname }
                                : null
                        };
                    }).ToList()
                };
            }).OrderByDescending(l => l.DataCriacao)
        };

        // 🆕 Salvar no cache
        lock (_cacheLock)
        {
            _cachePerfil[usuarioId] = (resultado, DateTime.UtcNow);
        }

        return resultado;
    }

    public async Task<List<object>> BuscarAsync(string termo)
    {
        if (string.IsNullOrWhiteSpace(termo) || termo.Length < 2)
            return new List<object>();

        var termoLower = termo.ToLower();

        // 🆕 Buscar com filtro no banco
        var usuarios = await _usuarioRepository.FindAsync(u => 
            u.Ativo && 
            ((u.Nome != null && u.Nome.ToLower().Contains(termoLower)) ||
             (u.Nickname != null && u.Nickname.ToLower().Contains(termoLower))));

        return usuarios
            .Take(20)
            .Select(u => new
            {
                u.Id,
                u.Nome,
                u.Nickname,
                u.Avatar,
                u.Descricao
            })
            .Cast<object>()
            .ToList();
    }

    public async Task UpdateFieldAsync(string id, string fieldName, object value)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("ID é obrigatório");

        if (string.IsNullOrEmpty(fieldName))
            throw new ArgumentException("Campo é obrigatório");

        await _usuarioRepository.UpdateFieldAsync(id, fieldName, value);

        // 🆕 Limpar cache do perfil
        LimparCachePerfil(id);
    }

    // 🆕 Método para limpar cache
    private void LimparCachePerfil(string usuarioId)
    {
        lock (_cacheLock)
        {
            _cachePerfil.Remove(usuarioId);
        }
    }
}