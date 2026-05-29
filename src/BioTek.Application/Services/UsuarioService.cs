using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using BioTek.Application.Services;
using System.Security.Cryptography.X509Certificates;
using System.Linq;


namespace BioTek.Application.Services;

public class UsuarioService : IUsuarioService
{
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;

    private readonly IRepository<ComentarioLivro> _comentarioRepository;

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
        var existente = await _usuarioRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _usuarioRepository.DeleteAsync(id);
        return true;
    }


    public async Task<Usuario> CriarAsync(Usuario usuario)
    {
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
        usuario.Adm = false;

        await _usuarioRepository.AddAsync(usuario);
        return usuario;
    }
 
    public async Task<Usuario?> AtualizarAsync(string id, Usuario usuario)
    {
        var existente = await _usuarioRepository.GetByIdAsync(id);
        if (existente == null) return null;

        // Preserva dados que não podem ser alterados
        existente.IdGoogle = existente.IdGoogle;
        existente.IdFacebook = existente.IdFacebook;
        existente.Email = existente.Email;
        existente.Adm = existente.Adm;
        existente.Ativo = existente.Ativo;
        existente.DataCriacao = existente.DataCriacao;

        // Atualiza campos editáveis
        existente.Nome = usuario.Nome;
        existente.Nickname = usuario.Nickname;
        existente.Descricao = usuario.Descricao;
        existente.Avatar = usuario.Avatar;
        existente.Tema = usuario.Tema;
        existente.AceitouTermos = usuario.AceitouTermos;        // ⬅️ NOVO
        existente.DataAceiteTermos = usuario.DataAceiteTermos;  // ⬅️ NOVO
        existente.DataAtualizacao = DateTime.UtcNow;

        await _usuarioRepository.UpdateAsync(id, existente);
        return existente;
    }

    public async Task<IEnumerable<Usuario>> GetUsuariosAsync()
    {
        var usuarios = await _usuarioRepository.GetAllAsync();
        return usuarios;
    }


    public async Task<Usuario?> GetPorIdAsync(string id)
    {
        return await _usuarioRepository.GetByIdAsync(id);
    }

    public async Task<Usuario?> GetPorNicknameAsync(string nickname)
    {
        var usuarios = await _usuarioRepository.FindAsync(u => u.Nickname == nickname);
        return usuarios.FirstOrDefault();
    }

   public async Task<object?> GetPerfilCompletoAsync(string usuarioId)
    {
        var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario == null) return null;

        var livros = await _livroRepository.FindAsync(l => l.IdUsuario == usuarioId);
        var avaliacoes = await _avaliacaoRepository.FindAsync(a => a.IdUsuario == usuarioId);
        var comentarios = await _comentarioRepository.GetAllAsync();
        var todosUsuarios = await _usuarioRepository.GetAllAsync();
        var usuarioMap = todosUsuarios.ToDictionary(u => u.Id);

        return new
        {
            usuario.Id,
            usuario.IdGoogle,
            usuario.Nome,
            usuario.Nickname,
            usuario.Email,
            usuario.Descricao,
            usuario.Avatar,
            usuario.Tema,
            usuario.Adm,
            usuario.Ativo,
            usuario.DataCriacao,
            usuario.DataAtualizacao,
            usuario.AceitouTermos,      // ⬅️ NOVO
            usuario.DataAceiteTermos,
            QuantidadeLivros = livros.Count(),
            QuantidadeLivrosLidos = livros.Count(l => l.Status == StatusLeitura.Lido),
            MediaAvaliacoes = avaliacoes.Any()
                ? Math.Round(avaliacoes.Average(a => (int)a.Nota), 1)
                : 0,
            Livros = livros.Select(l => new
            {
                l.Id,
                l.Titulo,
                l.Autor,
                l.Imagem,
                l.Status,
                l.DataCriacao,
                Avaliacao = avaliacoes.FirstOrDefault(a => a.IdLivro == l.Id) is { } av
                    ? new { av.Id, av.Nota, av.DataCriacao }
                    : null,
                Comentarios = comentarios
                    .Where(c => c.IdLivro == l.Id)
                    .Select(c => new
                    {
                        c.Id,
                        c.Comentario,
                        c.DataCriacao,
                        Usuario = usuarioMap.GetValueOrDefault(c.IdUsuario) is { } u ? new
                        {
                            u.Id,
                            u.Nickname
                        } : null
                    }).ToList()
            }).OrderByDescending(l => l.DataCriacao)
        };
    }
}