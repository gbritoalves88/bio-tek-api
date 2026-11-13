using System.Security.Claims;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

    // 🆕 GetUsuarioId melhorado
    private string GetUsuarioId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                 ?? User.FindFirst("sub")?.Value;
        
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("Usuário não autenticado");
        
        return id;
    }

    /// <summary>
    /// Lista todos os usuários (apenas admin)
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetAll()
    {
        var usuarioLogado = await _usuarioService.GetPorIdAsync(GetUsuarioId());
        if (usuarioLogado == null || !usuarioLogado.Adm)
            return Forbid();

        var usuarios = await _usuarioService.GetUsuariosAsync();
        return Ok(usuarios);
    }

    /// <summary>
    /// Busca usuário por ID (público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID é obrigatório" });

        var usuario = await _usuarioService.GetPorIdAsync(id);
        if (usuario == null)
            return NotFound();

        // 🆕 Retornar apenas dados públicos
        return Ok(new
        {
            usuario.Id,
            usuario.Nome,
            usuario.Nickname,
            usuario.Avatar,
            usuario.Descricao,
            usuario.DataCriacao
        });
    }

    /// <summary>
    /// Busca usuário por nickname (público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("nickname/{nickname}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPorNickname(string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            return BadRequest(new { mensagem = "Nickname é obrigatório" });

        var usuario = await _usuarioService.GetPorNicknameAsync(nickname);
        if (usuario == null) return NotFound();

        // 🆕 Retornar apenas dados públicos
        return Ok(new
        {
            usuario.Id,
            usuario.Nome,
            usuario.Nickname,
            usuario.Avatar,
            usuario.Descricao
        });
    }

    /// <summary>
    /// Criar usuário (público - cadastro)
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Create([FromBody] Usuario usuario)
    {
        try
        {
            var novoUsuario = await _usuarioService.CriarAsync(usuario);
            return CreatedAtAction(nameof(GetById), new { id = novoUsuario.Id }, novoUsuario);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    /// <summary>
    /// Atualizar usuário (apenas dono ou admin)
    /// </summary>
    [HttpPut("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Update(string id, [FromBody] Usuario usuario)
    {
        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID é obrigatório" });

        var usuarioLogadoId = GetUsuarioId();
        var usuarioLogado = await _usuarioService.GetPorIdAsync(usuarioLogadoId);

        if (usuarioLogado == null || (usuarioLogado.Id != id && !usuarioLogado.Adm))
            return Forbid();

        try // 🆕 Adicionar Try/Catch para pegar a duplicidade
        {
            var atualizado = await _usuarioService.AtualizarAsync(id, usuario);
            if (atualizado == null)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            // Retorna 400 com a mensagem "Este nickname já está em uso"
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    /// <summary>
    /// Deletar usuário (apenas dono ou admin)
    /// </summary>
    [HttpDelete("{id}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Delete(string id)
    {
        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID é obrigatório" });

        var usuarioLogadoId = GetUsuarioId();
        var usuarioLogado = await _usuarioService.GetPorIdAsync(usuarioLogadoId);

        // 🆕 Verificar se é o dono ou admin
        if (usuarioLogado == null || (usuarioLogado.Id != id && !usuarioLogado.Adm))
            return Forbid();

        var deletado = await _usuarioService.DeletarAsync(id);
        if (!deletado)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Perfil público completo de um usuário (com livros e avaliações)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id}/perfil-completo")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetPerfilCompleto(string id)
    {
        if (string.IsNullOrEmpty(id))
            return BadRequest(new { mensagem = "ID é obrigatório" });

        // 🆕 Pega o ID de quem está logado (se tiver token)
        string? usuarioLogadoId = null;
        try 
        { 
            usuarioLogadoId = GetUsuarioId(); 
        } 
        catch (UnauthorizedAccessException) 
        { 
            // Se não estiver logado, segue o fluxo como anônimo
        }

        var resultado = await _usuarioService.GetPerfilCompletoAsync(id, usuarioLogadoId);
        
        if (resultado == null) return NotFound();
        return Ok(resultado);
    }

    /// <summary>
    /// Aceitar termos de uso (usuário logado)
    /// </summary>
    [HttpPut("aceitar-termos")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> AceitarTermos()
    {
        var id = GetUsuarioId();
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
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetAllUsuarios()
    {
        var usuarioLogado = await _usuarioService.GetPorIdAsync(GetUsuarioId());
        
        if (usuarioLogado == null || !usuarioLogado.Adm)
            return Forbid();
        
        var usuarios = await _usuarioService.GetUsuariosAsync();
        return Ok(usuarios);
    }

    /// <summary>
    /// Busca usuários por nome ou nickname (público)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("buscar")]
    [EnableRateLimiting("GeneralPolicy")]
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
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Desativar()
    {
        var id = GetUsuarioId();
        var usuario = await _usuarioService.GetPorIdAsync(id);
        
        if (usuario == null) return NotFound();
        
        usuario.Ativo = false;
        usuario.DataAtualizacao = DateTime.UtcNow;
        
        await _usuarioService.AtualizarAsync(id, usuario);
        return Ok(new { mensagem = "Conta desativada." });
    }
}