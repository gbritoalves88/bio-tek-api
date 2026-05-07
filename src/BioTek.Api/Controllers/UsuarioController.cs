using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Infrastructure.Data;
using BioTek.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace BioTek.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsuarioController : ControllerBase
{
    private readonly IRepository<Usuario> _usuarioRepository;

    public UsuarioController(MongoDbContext context)
    {
        _usuarioRepository = new MongoRepository<Usuario>(context, "Usuarios");
    }

    // GET: api/usuario
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var usuarios = await _usuarioRepository.GetAllAsync();
        return Ok(usuarios);
    }

    // GET: api/usuario/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var usuario = await _usuarioRepository.GetByIdAsync(id);
        if (usuario == null)
            return NotFound();

        return Ok(usuario);
    }

    // POST: api/usuario
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Usuario usuario)
    {
        await _usuarioRepository.AddAsync(usuario);
        return CreatedAtAction(nameof(GetById), new { id = usuario.Id }, usuario);
    }

    // PUT: api/usuario/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] Usuario usuario)
    {
        var existente = await _usuarioRepository.GetByIdAsync(id);
        if (existente == null)
            return NotFound();

        usuario.Id = id;
        usuario.DataCriacao = existente.DataCriacao;
        usuario.DataAtualizacao = DateTime.UtcNow;

        await _usuarioRepository.UpdateAsync(id, usuario);
        return NoContent();
    }

    // DELETE: api/usuario/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var existente = await _usuarioRepository.GetByIdAsync(id);
        if (existente == null)
            return NotFound();

        await _usuarioRepository.DeleteAsync(id);
        return NoContent();
    }
}