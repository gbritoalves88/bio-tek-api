using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using BioTek.Application.Services;
using System.Security.Cryptography.X509Certificates;
using System.Linq;

namespace BioTek.Application.Services;

public class SeguidorService : ISeguidorService
{
    private readonly IRepository<Seguidor> _seguidorRepository;
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;


   public SeguidorService(
    IRepository<Seguidor> seguidorRepository,
    IRepository<Usuario> usuarioRepository,
    IRepository<Livro> livroRepository,
    IRepository<AvaliacaoLivro> avaliacaoRepository)
    {
        _seguidorRepository = seguidorRepository;
        _usuarioRepository = usuarioRepository;
        _livroRepository = livroRepository;
        _avaliacaoRepository = avaliacaoRepository;
    }

    public async Task<Seguidor> SeguirAsync(string idUsuario, string idSeguidor)
    {
        if (idUsuario == idSeguidor)
            throw new InvalidOperationException("Não pode seguir a si mesmo");

        var usuario = await _usuarioRepository.GetByIdAsync(idUsuario);
        var seguidor = await _usuarioRepository.GetByIdAsync(idSeguidor);
        if (usuario == null || seguidor == null)
            throw new InvalidOperationException("Usuário não encontrado");

        var existe = await _seguidorRepository.FindAsync(
            s => s.IdUsuario == idUsuario && s.IdSeguidor == idSeguidor);

        if (existe.Any())
            throw new InvalidOperationException("Você já segue este usuário");

        var novoSeguidor = new Seguidor
        {
            IdUsuario = idUsuario,
            IdSeguidor = idSeguidor,
            DataCriacao = DateTime.UtcNow
        };

        await _seguidorRepository.AddAsync(novoSeguidor);
        return novoSeguidor;
    }

    public async Task<bool> DeixarDeSeguirAsync(string idUsuario, string idSeguidor)
    {
        var vinculo = await _seguidorRepository.FindAsync(
            s => s.IdUsuario == idUsuario && s.IdSeguidor == idSeguidor);

        var item = vinculo.FirstOrDefault();
        if (item == null) return false;

        await _seguidorRepository.DeleteAsync(item.Id);
        return true;
    }

    public async Task<IEnumerable<Seguidor>> GetSeguidoresAsync(string idUsuario)
    {
        return await _seguidorRepository.FindAsync(s => s.IdUsuario == idUsuario);
    }

    public async Task<IEnumerable<Seguidor>> GetSeguindoAsync(string idSeguidor)
    {
        return await _seguidorRepository.FindAsync(s => s.IdSeguidor == idSeguidor);
    }

    public async Task<bool> EstaSeguindoAsync(string idUsuario, string idSeguidor)
    {
        var vinculo = await _seguidorRepository.FindAsync(
            s => s.IdUsuario == idUsuario && s.IdSeguidor == idSeguidor);
        return vinculo.Any();
    }

    public async Task<List<object>> GetSeguindoCompletoAsync(string idSeguidor)
    {
        var seguindo = await _seguidorRepository.FindAsync(s => s.IdSeguidor == idSeguidor);
        var usuarios = await _usuarioRepository.GetAllAsync();
        var livros = await _livroRepository.GetAllAsync();
        var avaliacoes = await _avaliacaoRepository.GetAllAsync();

        var usuarioMap = usuarios.ToDictionary(u => u.Id);

        return seguindo.Select(s =>
        {
            var u = usuarioMap.GetValueOrDefault(s.IdUsuario);
            if (u == null) return null;

            var livrosDoUsuario = livros.Where(l => l.IdUsuario == u.Id).ToList();
            var avaliacoesDoUsuario = avaliacoes.Where(a => a.IdUsuario == u.Id).ToList();

            return (object)new
            {
                u.Id,
                u.Nome,
                u.Nickname,
                u.Avatar,
                u.Descricao,
                QuantidadeLivros = livrosDoUsuario.Count,
                QuantidadeLivrosLidos = livrosDoUsuario.Count(l => l.Status == StatusLeitura.Lido),
                MediaAvaliacoes = avaliacoesDoUsuario.Any()
                    ? Math.Round(avaliacoesDoUsuario.Average(a => (int)a.Nota), 1)
                    : 0
            };
        }).Where(u => u != null).Cast<object>().ToList();
    }
}