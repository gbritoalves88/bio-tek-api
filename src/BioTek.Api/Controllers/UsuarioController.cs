using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Infrastructure.Data;
using BioTek.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BioTek.Application.Services;
using System.Security.Claims;

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

    private string GetUsuarioId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
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

    /// <summary>
    /// Perfil público completo de um usuário (com livros e avaliações)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id}/perfil-completo")]
    public async Task<IActionResult> GetPerfilCompleto(string id)
    {
        var resultado = await _usuarioService.GetPerfilCompletoAsync(id);
        if (resultado == null) return NotFound();
        return Ok(resultado);
    }

    /// <summary>
    /// Aceitar termos de uso (usuário logado)
    /// </summary>
    [HttpPut("aceitar-termos")]
    public async Task<IActionResult> AceitarTermos()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var usuario = await _usuarioService.GetPorIdAsync(id);
        if (usuario == null) return NotFound();

        await _usuarioService.UpdateFieldAsync(id, "AceitouTermos", true);
        await _usuarioService.UpdateFieldAsync(id, "DataAceiteTermos", DateTime.UtcNow);
        await _usuarioService.UpdateFieldAsync(id, "Ativo", true);

        return Ok(new { mensagem = "Termos aceitos com sucesso." });
    }
    

    /// <summary>
    /// Lista todos os usuários (apenas para admin)
    /// </summary>
    [Authorize]
    [HttpGet("todos")]
    public async Task<IActionResult> GetAllUsuarios()
    {
        var usuarioLogado = await _usuarioService.GetPorIdAsync(GetUsuarioId());
        
        if (usuarioLogado == null || !usuarioLogado.Adm)
            return Forbid("Acesso apenas para administradores");
        
        var usuarios = await _usuarioService.GetUsuariosAsync();
        return Ok(usuarios);
    }

    /// <summary>
    /// Busca usuários por nome ou nickname (público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(new List<object>());

        var usuarios = await _usuarioService.BuscarAsync(q);
        return Ok(usuarios);
    }

    /// <summary>
    /// Desativa a conta do usuário (quando recusa os termos)
    /// </summary>
    [HttpPost("desativar")]
    public async Task<IActionResult> Desativar()
    {
        var id = GetUsuarioId();
        var usuario = await _usuarioService.GetPorIdAsync(id);
        
        if (usuario == null) return NotFound();
        
        usuario.Ativo = false;
        usuario.AceitouTermos = false;
        usuario.DataAtualizacao = DateTime.UtcNow;
        
        await _usuarioService.AtualizarAsync(id, usuario);
        return Ok(new { mensagem = "Conta desativada." });
    }
}