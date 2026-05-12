using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Infrastructure.Data;
using BioTek.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BioTek.Application.Services;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UsuarioController : ControllerBase
{
    private readonly IUsuarioService _usuarioService;

    public UsuarioController(IUsuarioService usuarioService)
    {
        _usuarioService = usuarioService;
    }

    // GET: api/usuario
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var usuarios = await _usuarioService.GetUsuariosAsync();
        return Ok(usuarios);
    }

    // GET: api/usuario/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var usuario = await _usuarioService.GetPorIdAsync(id);
        if (usuario == null)
            return NotFound();

        return Ok(usuario);
    }

    // GET: api/Usuario/nickname/{nickname}
    [HttpGet("nickname/{nickname}")]
    public async Task<IActionResult> GetPorNickname(string nickname)
    {
        var usuario = await _usuarioService.GetPorNicknameAsync(nickname);
        if (usuario == null) return NotFound();
        return Ok(usuario);
    }

    // POST: api/usuario
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Usuario usuario)
    {
        await _usuarioService.CriarAsync(usuario);
        return CreatedAtAction(nameof(GetById), new { id = usuario.Id }, usuario);
    }

    // PUT: api/usuario/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] Usuario usuario)
    {
        await _usuarioService.AtualizarAsync(id, usuario);
        
        return NoContent();
    }

    // DELETE: api/usuario/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _usuarioService.DeletarAsync(id);

        return NoContent();
    }
}