using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;

namespace BioTek.Application.Services;

public class ColecaoService : IColecaoService
{
    private readonly IRepository<Colecao> _colecaoRepository;
    private readonly IRepository<Livro> _livroRepository;

    // 🆕 Cache do dashboard (mesmo padrão do Home)
    private static object? _cacheColecoes;
    private static string? _cacheUsuarioId;
    private static DateTime _cacheTime = DateTime.MinValue;
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromSeconds(30);

    public ColecaoService(
        IRepository<Colecao> colecaoRepository,
        IRepository<Livro> livroRepository)
    {
        _colecaoRepository = colecaoRepository;
        _livroRepository = livroRepository;
    }

    // ============ LISTAGEM ============
    public async Task<List<object>> GetColecoesAsync(string usuarioId)
    {
        // 🆕 Cache
        lock (_cacheLock)
        {
            if (_cacheColecoes != null &&
                _cacheUsuarioId == usuarioId &&
                DateTime.UtcNow - _cacheTime < CACHE_DURATION)
            {
                return (List<object>)_cacheColecoes;
            }
        }

        var colecoes = (await _colecaoRepository.FindAsync(c => c.IdUsuario == usuarioId)).ToList();

        if (colecoes.Count == 0)
        {
            var vazio = new List<object>();
            SalvarCache(vazio, usuarioId);
            return vazio;
        }

        // 🆕 Busca os livros do usuário (que têm prateleira)
        var livros = (await _livroRepository.FindAsync(l =>
            l.IdUsuario == usuarioId && l.IdColecao != null)).ToList();

        // 🆕 Projeção
        var resultado = colecoes
            .OrderBy(c => c.Nome)
            .Select(c =>
            {
                var livrosDaColecao = livros
                    .Where(l => l.IdColecao == c.Id)
                    .ToList();

                return (object)new
                {
                    c.Id,
                    c.Nome,
                    c.Descricao,
                    c.DataCriacao,
                    TotalLivros = livrosDaColecao.Count,
                    // 🆕 3 primeiras capas (ordem: mais recentes)
                    Capas = livrosDaColecao
                        .Where(l => !string.IsNullOrEmpty(l.Imagem))
                        .OrderByDescending(l => l.DataCriacao)
                        .Take(3)
                        .Select(l => l.Imagem)
                        .ToList()
                };
            })
            .ToList();

        SalvarCache(resultado, usuarioId);
        return resultado;
    }

    // ============ BUSCAR POR ID ============
    public async Task<Colecao?> GetPorIdAsync(string id, string usuarioId)
    {
        var colecao = await _colecaoRepository.GetByIdAsync(id);
        if (colecao == null) return null;

        // 🆕 Verifica se é do usuário
        if (colecao.IdUsuario != usuarioId) return null;

        return colecao;
    }

    // ============ CRIAR ============
    public async Task<Colecao> CriarAsync(Colecao colecao)
    {
        // 🆕 Validações
        if (string.IsNullOrWhiteSpace(colecao.Nome))
            throw new InvalidOperationException("Nome da prateleira é obrigatório");

        if (colecao.Nome.Length > 50)
            throw new InvalidOperationException("Nome da prateleira deve ter no máximo 50 caracteres");

        // 🆕 Nome único POR usuário (não global)
        var existe = await _colecaoRepository.FindAsync(c =>
            c.IdUsuario == colecao.IdUsuario &&
            c.Nome.ToLower() == colecao.Nome.ToLower());

        if (existe.Any())
            throw new InvalidOperationException("Você já tem uma prateleira com este nome");

        colecao.Id = Guid.NewGuid().ToString();
        colecao.DataCriacao = DateTime.UtcNow;

        await _colecaoRepository.AddAsync(colecao);
        LimparCache();
        return colecao;
    }

    // ============ ATUALIZAR ============
    public async Task<Colecao?> AtualizarAsync(string id, string usuarioId, Colecao colecao)
    {
        var existente = await _colecaoRepository.GetByIdAsync(id);
        if (existente == null) return null;

        // 🆕 Verifica se é do usuário
        if (existente.IdUsuario != usuarioId) return null;

        // 🆕 Validações
        if (string.IsNullOrWhiteSpace(colecao.Nome))
            throw new InvalidOperationException("Nome da prateleira é obrigatório");

        // 🆕 Nome único (exceto ele mesmo)
        var existe = await _colecaoRepository.FindAsync(c =>
            c.IdUsuario == usuarioId &&
            c.Id != id &&
            c.Nome.ToLower() == colecao.Nome.ToLower());

        if (existe.Any())
            throw new InvalidOperationException("Você já tem uma prateleira com este nome");

        // 🆕 Preserva campos de sistema
        colecao.Id = id;
        colecao.IdUsuario = existente.IdUsuario;
        colecao.DataCriacao = existente.DataCriacao;
        colecao.DataAtualizacao = DateTime.UtcNow;

        await _colecaoRepository.UpdateAsync(id, colecao);
        LimparCache();
        return colecao;
    }

    // ============ DELETAR ============
    public async Task<bool> DeletarAsync(string id, string usuarioId)
    {
        var existente = await _colecaoRepository.GetByIdAsync(id);
        if (existente == null) return false;

        // 🆕 Verifica se é do usuário
        if (existente.IdUsuario != usuarioId) return false;

        // 🆕 Desvincula os livros da prateleira (não deleta livros)
        var livros = await _livroRepository.FindAsync(l => l.IdColecao == id);
        foreach (var livro in livros)
        {
            livro.IdColecao = null;
            livro.DataAtualizacao = DateTime.UtcNow;
            await _livroRepository.UpdateAsync(livro.Id, livro);
        }

        await _colecaoRepository.DeleteAsync(id);
        LimparCache();
        return true;
    }

    // ============ CACHE ============
    private void SalvarCache(object dados, string usuarioId)
    {
        lock (_cacheLock)
        {
            _cacheColecoes = dados;
            _cacheUsuarioId = usuarioId;
            _cacheTime = DateTime.UtcNow;
        }
    }

    public void LimparCache()
    {
        lock (_cacheLock)
        {
            _cacheColecoes = null;
            _cacheUsuarioId = null;
            _cacheTime = DateTime.MinValue;
        }
    }
}