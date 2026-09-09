using System.Security.Claims;
using BioTek.Api.Models;
using BioTek.Domain.Interfaces;
using BioTek.Models.DTOs;
using BioTek.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class NotificacaoController : ControllerBase
{
    private readonly INotificacaoService _notificacaoService;
    private readonly IFirebaseNotificationService _firebaseNotificationService;
    private readonly IUsuarioService _usuarioService;
    private readonly ILogger<NotificacaoController> _logger;

    public NotificacaoController(
        INotificacaoService notificacaoService,
        IFirebaseNotificationService firebaseNotificationService,
        IUsuarioService usuarioService,
        ILogger<NotificacaoController> logger)
    {
        _notificacaoService = notificacaoService;
        _firebaseNotificationService = firebaseNotificationService;
        _usuarioService = usuarioService;
        _logger = logger;
    }

    #region Métodos Auxiliares

    private string GetUsuarioId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                 ?? User.FindFirst("sub")?.Value;
        
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("Usuário não autenticado");
        
        return id;
    }

    private async Task<bool> IsAdminAsync()
    {
        var usuario = await _usuarioService.GetPorIdAsync(GetUsuarioId());
        return usuario?.Adm == true;
    }

    private IActionResult ForbiddenResult(string message = "Apenas administradores podem realizar esta ação")
    {
        return StatusCode(StatusCodes.Status403Forbidden, new { message });
    }

    #endregion

    #region Notificações Internas

    /// <summary>
    /// Buscar notificações não lidas
    /// </summary>
    [HttpGet("nao-lidas")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetNaoLidas()
    {
        try
        {
            var notificacoes = await _notificacaoService.GetNaoLidasAsync(GetUsuarioId());
            return Ok(notificacoes);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Usuário não autenticado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar notificações não lidas");
            return StatusCode(500, new { message = "Erro interno ao buscar notificações" });
        }
    }

    /// <summary>
    /// Buscar todas as notificações
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var notificacoes = await _notificacaoService.GetAllAsync(GetUsuarioId());
            return Ok(notificacoes);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Usuário não autenticado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar todas as notificações");
            return StatusCode(500, new { message = "Erro interno ao buscar notificações" });
        }
    }

    /// <summary>
    /// Marcar todas como lidas
    /// </summary>
    [HttpPost("marcar-lidas")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> MarcarLidas()
    {
        try
        {
            await _notificacaoService.MarcarComoLidasAsync(GetUsuarioId());
            return Ok(new { message = "Notificações marcadas como lidas" });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Usuário não autenticado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao marcar notificações como lidas");
            return StatusCode(500, new { message = "Erro interno ao marcar notificações" });
        }
    }

    #endregion

    #region Push Notifications

    /// <summary>
    /// Registrar subscription para push notifications
    /// </summary>
    [HttpPost("subscribe")]
    [EnableRateLimiting("NotificationPolicy")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request)
    {
        try
        {
            if (request == null)
                return BadRequest(new { message = "Dados são obrigatórios" });

            // Sempre usar o usuário autenticado, nunca o do request
            request.UserId = GetUsuarioId();

            // Validações
            if (string.IsNullOrWhiteSpace(request.Token))
                return BadRequest(new { message = "Token é obrigatório" });

            if (request.Token.Length > 1000)
                return BadRequest(new { message = "Token inválido" });

            if (string.IsNullOrWhiteSpace(request.Platform))
                request.Platform = "web";

            if (string.IsNullOrWhiteSpace(request.Device))
                request.Device = "unknown";

            var result = await _notificacaoService.SubscribeAsync(request);

            if (result)
                return Ok(new { success = true, message = "Subscription registrada com sucesso" });
            else
                return BadRequest(new { success = false, message = "Erro ao registrar subscription" });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Usuário não autenticado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao registrar subscription");
            return StatusCode(500, new { message = "Erro interno ao registrar subscription" });
        }
    }

    /// <summary>
    /// Cancelar subscription de push notifications
    /// </summary>
    [HttpPost("unsubscribe")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest request)
    {
        try
        {
            if (request == null)
                return BadRequest(new { message = "Dados são obrigatórios" });

            // Sempre usar o usuário autenticado
            request.UserId = GetUsuarioId();

            var result = await _notificacaoService.UnsubscribeAsync(request);

            if (result)
                return Ok(new { success = true, message = "Subscription cancelada com sucesso" });
            else
                return BadRequest(new { success = false, message = "Erro ao cancelar subscription" });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Usuário não autenticado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao cancelar subscription");
            return StatusCode(500, new { message = "Erro interno ao cancelar subscription" });
        }
    }

    /// <summary>
    /// Buscar subscriptions do usuário
    /// </summary>
    [HttpGet("subscriptions")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> GetUserSubscriptions()
    {
        try
        {
            var subscriptions = await _notificacaoService.GetUserSubscriptionsAsync(GetUsuarioId());
            return Ok(subscriptions);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Usuário não autenticado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar subscriptions");
            return StatusCode(500, new { message = "Erro interno ao buscar subscriptions" });
        }
    }

    /// <summary>
    /// Testar push notification (apenas admin)
    /// </summary>
    [HttpPost("test-push")]
    [EnableRateLimiting("NotificationPolicy")]
    public async Task<IActionResult> TestPushNotification([FromBody] SubscribeRequest request)
    {
        try
        {
            // Apenas admin pode testar
            if (!await IsAdminAsync())
                return ForbiddenResult();

            if (request == null || string.IsNullOrWhiteSpace(request.Token))
                return BadRequest(new { message = "Token é obrigatório" });

            var success = await _firebaseNotificationService.SendNotificationAsync(
                request.Token,
                "🔔 Teste de Notificação",
                "Se você está vendo isso, as notificações estão funcionando!",
                "/",
                new Dictionary<string, string> { { "test", "true" } }
            );

            return Ok(new { success, message = "Notificação de teste enviada" });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Usuário não autenticado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar notificação de teste");
            return StatusCode(500, new { message = "Erro interno ao enviar notificação de teste" });
        }
    }

    /// <summary>
    /// Enviar notificação para usuário específico (apenas admin)
    /// </summary>
    [HttpPost("enviar-para-usuario")]
    [EnableRateLimiting("NotificationPolicy")]
    public async Task<IActionResult> EnviarParaUsuario([FromBody] EnviarNotificacaoRequest request)
    {
        try
        {
            // Verificar se é admin
            if (!await IsAdminAsync())
                return ForbiddenResult();

            // Validações
            if (request == null || string.IsNullOrWhiteSpace(request.UserId))
                return BadRequest(new { message = "UserId é obrigatório" });

            if (string.IsNullOrWhiteSpace(request.Title))
                return BadRequest(new { message = "Título é obrigatório" });

            if (string.IsNullOrWhiteSpace(request.Body))
                return BadRequest(new { message = "Mensagem é obrigatória" });

            // Log de auditoria
            var admin = await _usuarioService.GetPorIdAsync(GetUsuarioId());
            _logger.LogInformation($"Admin {admin?.Nome} enviou notificação para usuário {request.UserId}");

            // Buscar tokens ativos do usuário
            var subscriptions = await _notificacaoService.GetUserSubscriptionsAsync(request.UserId);

            if (subscriptions == null || subscriptions.Count == 0)
                return NotFound(new { success = false, message = "Nenhuma subscription ativa encontrada para este usuário" });

            var tokens = subscriptions.Select(s => s.Token).ToList();

            var success = await _firebaseNotificationService.SendMulticastAsync(
                tokens,
                request.Title,
                request.Body,
                request.Url,
                request.Data
            );

            return Ok(new
            {
                success,
                message = $"Notificações enviadas para {tokens.Count} dispositivo(s)"
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Usuário não autenticado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar notificação para usuário");
            return StatusCode(500, new { message = "Erro interno ao enviar notificação" });
        }
    }

    #endregion

    /// <summary>
    /// Registrar decisão de notificação
    /// </summary>
    [HttpPost("registrar-decisao")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> RegistrarDecisao([FromBody] RegistrarDecisaoRequest request)
    {
        try
        {
            if (request == null)
                return BadRequest(new { message = "Dados são obrigatórios" });

            // Sempre usar o usuário autenticado
            request.UserId = GetUsuarioId();
            
            var usuario = await _usuarioService.GetPorIdAsync(request.UserId);
            if (usuario == null)
                return NotFound(new { message = "Usuário não encontrado" });
            
            await _usuarioService.AtualizarAsync(usuario.Id, usuario);
            
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao registrar decisão de notificação");
            return StatusCode(500, new { message = "Erro interno" });
        }
    }
}