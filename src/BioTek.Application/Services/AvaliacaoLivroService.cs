using BioTek.Domain.Entities;
using BioTek.Domain.Enums;
using BioTek.Domain.Interfaces;
using BioTek.Application.Services;
using System.Security.Cryptography.X509Certificates;
using System.Linq;


namespace BioTek.Application.Services;

public class AvaliacaoLivroService : IAvaliacaoLivroService
{
    private readonly IRepository<AvaliacaoLivro> _avaliacaoLivroRepository;
    private readonly ILivroService _livroService;

    public AvaliacaoLivroService(IRepository<AvaliacaoLivro> avaliacaoLivroRepository, ILivroService livroService)
    {
        _avaliacaoLivroRepository = avaliacaoLivroRepository;
        _livroService = livroService;
    }



    public async Task<bool> DeletarAsync(string id)
    {
        var existente = await _avaliacaoLivroRepository.GetByIdAsync(id);
        if (existente == null) return false;

        await _avaliacaoLivroRepository.DeleteAsync(id);
        return true;
    }


    public async Task<AvaliacaoLivro> CriarAsync(AvaliacaoLivro avaliacaoLivro)
    {
        Console.WriteLine($"🔍 IdUsuario: {avaliacaoLivro.IdUsuario}");
        Console.WriteLine($"📖 IdLivro: {avaliacaoLivro.IdLivro}");

        var livrosDoUsuario = await _livroService.GetPorUsuarioAsync(avaliacaoLivro.IdUsuario);
        Console.WriteLine($"📚 Livros encontrados: {livrosDoUsuario.Count()}");

        foreach (var l in livrosDoUsuario)
            Console.WriteLine($"   ID: {l.Id} | Título: {l.Titulo} | Status: {l.Status}");

        var livro = ((IEnumerable<Livro>)livrosDoUsuario).FirstOrDefault(l => l.Id == avaliacaoLivro.IdLivro);

        if (livro == null)
            throw new InvalidOperationException("Livro não encontrado");

        if (livro.Status != StatusLeitura.Lido)
            throw new InvalidOperationException("Só pode avaliar livros com status Lido");

        avaliacaoLivro.Id = Guid.NewGuid().ToString();
        avaliacaoLivro.DataCriacao = DateTime.UtcNow;

        await _avaliacaoLivroRepository.AddAsync(avaliacaoLivro);
        return avaliacaoLivro;
    }

    public async Task<AvaliacaoLivro?> AtualizarAsync(string id, AvaliacaoLivro avaliacaoLivro)
    {
        var existente = await _avaliacaoLivroRepository.GetByIdAsync(id);
        if (existente == null) return null;


        existente.Nota = avaliacaoLivro.Nota;
        existente.DataAtualizacao = DateTime.UtcNow;


        await _avaliacaoLivroRepository.UpdateAsync(id, existente);
        return existente;
    }
    public async Task<IEnumerable<AvaliacaoLivro>> GetPorUsuarioAsync(string usuarioIdAvaliacoes)
    {
        return await _avaliacaoLivroRepository.FindAsync(l => l.IdUsuario == usuarioIdAvaliacoes);
    }

    public async Task<IEnumerable<AvaliacaoLivro>> GetPorLivroAsync(string livroId)
    {
        return await _avaliacaoLivroRepository.FindAsync(a => a.IdLivro == livroId);
    }

    public async Task<AvaliacaoLivro?> GetPorIdAsync(string id)
    {
        return await _avaliacaoLivroRepository.GetByIdAsync(id);
    }

   public async Task<IEnumerable<AvaliacaoLivro>> GetTodasAsync()
    {
        return await _avaliacaoLivroRepository.GetAllAsync();
    }
    
}