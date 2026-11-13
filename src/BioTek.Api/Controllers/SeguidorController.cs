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
public class SeguidorController : ControllerBase
{
    private readonly ISeguidorService _seguidorService;
    private readonly INotificacaoService _notificacaoService;
    private readonly IUsuarioService _usuarioService;

    public SeguidorController(
        ISeguidorService seguidorService,
        IUsuarioService usuarioService,
        INotificacaoService notificacaoService)
    {
        _seguidorService = seguidorService;
        _usuarioService = usuarioService;
        _notificacaoService = notificacaoService;
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
    /// Seguir um usuário
    /// </summary>
    [HttpPost("seguir/{idUsuario}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Seguir(string idUsuario)
    {
        var usuarioLogadoId = GetUsuarioId();
        try
        {
            var resultado = await _seguidorService.SeguirAsync(idUsuario, usuarioLogadoId);
            
            // 🆕 Limpa o cache do perfil AQUI, e não no Service
            _usuarioService.LimparCachePerfil(idUsuario); 

            // Notificação em background (opcional, mas ajuda a não travar)
            _ = Task.Run(async () => {
                using var scope = HttpContext.RequestServices.CreateScope();
                var notificacao = scope.ServiceProvider.GetRequiredService<INotificacaoService>();
                var usuario = await _usuarioService.GetPorIdAsync(usuarioLogadoId);
                await notificacao.CriarNotificacaoSeguidor(idUsuario, usuarioLogadoId, usuario?.Nome ?? "Alguém");
            });

            return Ok(resultado);
        }
        catch (Exception ex) { return BadRequest(new { mensagem = ex.Message }); }

    }

    /// <summary>
    /// Deixar de seguir um usuário
    /// </summary>
    [HttpDelete("deixar-de-seguir/{idUsuario}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> DeixarDeSeguir(string idUsuario)
    {
         var result = await _seguidorService.DeixarDeSeguirAsync(idUsuario, GetUsuarioId());
    
        // 🆕 Limpa o cache
        if (result) _usuarioService.LimparCachePerfil(idUsuario);

        return result ? NoContent() : NotFound();
    }

    /// <summary>
    /// Quem o usuário logado está seguindo
    /// </summary>
    [HttpGet("seguindo")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetSeguindo()
    {
        var seguindo = await _seguidorService.GetSeguindoAsync(GetUsuarioId());
        return Ok(seguindo);
    }

    /// <summary>
    /// Quem segue o usuário logado
    /// </summary>
    [HttpGet("seguidores")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetSeguidores()
    {
        var seguidores = await _seguidorService.GetSeguidoresAsync(GetUsuarioId());
        return Ok(seguidores);
    }

    /// <summary>
    /// Verificar se o usuário logado segue outro usuário
    /// </summary>
    [HttpGet("esta-seguindo/{idUsuario}")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> EstaSeguindo(string idUsuario)
    {
        if (string.IsNullOrEmpty(idUsuario))
            return BadRequest(new { mensagem = "ID do usuário é obrigatório" });

        var result = await _seguidorService.EstaSeguindoAsync(idUsuario, GetUsuarioId());
        return Ok(new { seguindo = result });
    }

    /// <summary>
    /// Lista completa de quem o usuário logado segue (com dados dos usuários)
    /// </summary>
    [HttpGet("seguindo-completo")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetSeguindoCompleto()
    {
        var idSeguidor = GetUsuarioId();
        var seguindo = await _seguidorService.GetSeguindoCompletoAsync(idSeguidor);
        return Ok(seguindo);
    }
}