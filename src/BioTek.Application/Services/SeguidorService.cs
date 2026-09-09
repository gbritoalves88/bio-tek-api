using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class SeguidorService : ISeguidorService
{
    private readonly IRepository<Seguidor> _seguidorRepository;
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IRepository<Livro> _livroRepository;
    private readonly IRepository<AvaliacaoLivro> _avaliacaoRepository;

    // 🆕 Cache do seguindo completo
    private static readonly Dictionary<string, (List<object> Data, DateTime Time)> _cacheSeguindoCompleto = new();
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(5);

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
        // 🆕 Validação de entrada
        if (string.IsNullOrEmpty(idUsuario) || string.IsNullOrEmpty(idSeguidor))
            throw new ArgumentException("IDs são obrigatórios");

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
            Id = Guid.NewGuid().ToString(), // 🆕 Adicionar ID
            IdUsuario = idUsuario,
            IdSeguidor = idSeguidor,
            DataCriacao = DateTime.UtcNow
        };

        await _seguidorRepository.AddAsync(novoSeguidor);

        // 🆕 Limpar cache do seguindo completo
        LimparCacheSeguindoCompleto(idSeguidor);

        return novoSeguidor;
    }

    public async Task<bool> DeixarDeSeguirAsync(string idUsuario, string idSeguidor)
    {
        // 🆕 Validação de entrada
        if (string.IsNullOrEmpty(idUsuario) || string.IsNullOrEmpty(idSeguidor))
            throw new ArgumentException("IDs são obrigatórios");

        var vinculo = await _seguidorRepository.FindAsync(
            s => s.IdUsuario == idUsuario && s.IdSeguidor == idSeguidor);

        var item = vinculo.FirstOrDefault();
        if (item == null) return false;

        await _seguidorRepository.DeleteAsync(item.Id);

        // 🆕 Limpar cache do seguindo completo
        LimparCacheSeguindoCompleto(idSeguidor);

        return true;
    }

    public async Task<IEnumerable<Seguidor>> GetSeguidoresAsync(string idUsuario)
    {
        if (string.IsNullOrEmpty(idUsuario))
            return new List<Seguidor>();

        return await _seguidorRepository.FindAsync(s => s.IdUsuario == idUsuario);
    }

    public async Task<IEnumerable<Seguidor>> GetSeguindoAsync(string idSeguidor)
    {
        if (string.IsNullOrEmpty(idSeguidor))
            return new List<Seguidor>();

        return await _seguidorRepository.FindAsync(s => s.IdSeguidor == idSeguidor);
    }

    public async Task<bool> EstaSeguindoAsync(string idUsuario, string idSeguidor)
    {
        if (string.IsNullOrEmpty(idUsuario) || string.IsNullOrEmpty(idSeguidor))
            return false;

        var vinculo = await _seguidorRepository.FindAsync(
            s => s.IdUsuario == idUsuario && s.IdSeguidor == idSeguidor);
        return vinculo.Any();
    }

    public async Task<List<object>> GetSeguindoCompletoAsync(string idSeguidor)
    {
        if (string.IsNullOrEmpty(idSeguidor))
            return new List<object>();

        // 🆕 Verificar cache
        lock (_cacheLock)
        {
            if (_cacheSeguindoCompleto.TryGetValue(idSeguidor, out var cached) && 
                DateTime.UtcNow - cached.Time < CACHE_DURATION)
            {
                return cached.Data;
            }
        }

        // ============ 1. BUSCAR SEGUINDO COM FILTRO ============
        var seguindo = await _seguidorRepository.FindAsync(s => s.IdSeguidor == idSeguidor);
        
        if (!seguindo.Any())
        {
            var resultadoVazio = new List<object>();
            lock (_cacheLock)
            {
                _cacheSeguindoCompleto[idSeguidor] = (resultadoVazio, DateTime.UtcNow);
            }
            return resultadoVazio;
        }

        var idsUsuariosSeguidos = seguindo.Select(s => s.IdUsuario).ToHashSet();

        // ============ 2. BUSCAR DADOS RELACIONADOS EM PARALELO ============
        var tarefas = new
        {
            Usuarios = _usuarioRepository.FindAsync(u => idsUsuariosSeguidos.Contains(u.Id)),
            Livros = _livroRepository.FindAsync(l => idsUsuariosSeguidos.Contains(l.IdUsuario)),
            Avaliacoes = _avaliacaoRepository.FindAsync(a => idsUsuariosSeguidos.Contains(a.IdUsuario))
        };

        await Task.WhenAll(tarefas.Usuarios, tarefas.Livros, tarefas.Avaliacoes);

        var usuarios = await tarefas.Usuarios;
        var livros = await tarefas.Livros;
        var avaliacoes = await tarefas.Avaliacoes;

        // ============ 3. AGRUPAR DADOS ============
        var livrosPorUsuario = livros
            .GroupBy(l => l.IdUsuario)
            .ToDictionary(g => g.Key, g => g.ToList());

        var avaliacoesPorUsuario = avaliacoes
            .GroupBy(a => a.IdUsuario)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ============ 4. MONTAR RESULTADO ============
        var resultado = new List<object>();

        foreach (var u in usuarios)
        {
            var livrosDoUsuario = livrosPorUsuario.GetValueOrDefault(u.Id, new List<Livro>());
            var avaliacoesDoUsuario = avaliacoesPorUsuario.GetValueOrDefault(u.Id, new List<AvaliacaoLivro>());

            var item = new
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

            resultado.Add(item);
        }

        // Ordenar por nickname para consistência
        var resultadoOrdenado = resultado
            .OrderBy(r => ((dynamic)r).Nickname?.ToLower())
            .ToList();

        // 🆕 Salvar no cache
        lock (_cacheLock)
        {
            _cacheSeguindoCompleto[idSeguidor] = (resultadoOrdenado, DateTime.UtcNow);
        }

        return resultadoOrdenado;
    }

    // 🆕 Método para limpar cache
    private void LimparCacheSeguindoCompleto(string idSeguidor)
    {
        lock (_cacheLock)
        {
            _cacheSeguindoCompleto.Remove(idSeguidor);
        }
    }
}