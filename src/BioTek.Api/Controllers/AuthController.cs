using BioTek.Api.Models;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Infrastructure.Data;
using BioTek.Infrastructure.Repositories;
using BioTek.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Cryptography;

namespace BioTek.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("AuthPolicy")]
public class AuthController : ControllerBase
{
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly IRepository<Acesso> _acessoRepository;
    private readonly TokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    private const string CONTA_DESATIVADA_MENSAGEM = "Conta desativada. Entre em contato com os administradores pelo email: g.britoalves888@gmail.com";

    public AuthController(
        MongoDbContext context,
        TokenService tokenService,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _usuarioRepository = new MongoRepository<Usuario>(context, "Usuarios");
        _acessoRepository = new MongoRepository<Acesso>(context, "Acessos");
        _tokenService = tokenService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Login com Google (ID Token) — fluxo nativo (Capacitor)
    /// </summary>
    [AllowAnonymous]
    [HttpPost("google")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> LoginComGoogle([FromBody] SocialLoginRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.IdToken))
            return BadRequest(new { mensagem = "Token do Google é obrigatório" });

        if (request.IdToken.Length > 10000)
            return BadRequest(new { mensagem = "Token inválido" });

        try
        {
            // Valida contra o ClientId padrão (Android)
            var payload = await ValidarGoogleTokenAsync(request.IdToken);
            if (payload == null)
                return Unauthorized(new { mensagem = "Token do Google inválido" });

            var googleId = payload.Subject;
            var nome = payload.Name;
            var email = payload.Email;

            if (string.IsNullOrEmpty(email))
                return BadRequest(new { mensagem = "Email não fornecido pelo Google" });

            var usuarios = await _usuarioRepository.FindAsync(u => u.IdGoogle == googleId || u.Email == email);
            var usuario = usuarios.FirstOrDefault();
            bool novoUsuario = false;

            if (usuario == null)
            {
                novoUsuario = true;
                usuario = new Usuario
                {
                    Id = Guid.NewGuid().ToString(),
                    IdGoogle = googleId,
                    Nome = nome,
                    Email = email,
                    DataCriacao = DateTime.UtcNow,
                    DataAtualizacao = DateTime.UtcNow,
                    Ativo = true,
                    Adm = false,
                    AceitouTermos = false
                };
                await _usuarioRepository.AddAsync(usuario);
            }
            else
            {
                if (usuario.Ativo == false)
                {
                    _logger.LogWarning($"Tentativa de login com conta desativada: {email}");
                    return Unauthorized(new { mensagem = CONTA_DESATIVADA_MENSAGEM });
                }

                usuario.Nome = nome;
                usuario.IdGoogle ??= googleId;
                usuario.DataAtualizacao = DateTime.UtcNow;
            }

            var token = _tokenService.GerarToken(usuario.Id, usuario.Nome, usuario.Email, "google");
            var refreshToken = GerarRefreshTokenSeguro();

            usuario.RefreshToken = refreshToken;
            usuario.DataAtualizacao = DateTime.UtcNow;
            await _usuarioRepository.UpdateAsync(usuario.Id, usuario);

            _ = Task.Run(() => RegistrarAcessoAsync(usuario.Id));

            return Ok(new
            {
                token,
                refreshToken,
                novoUsuario = novoUsuario || !usuario.AceitouTermos,
                precisaAceitarTermos = !usuario.AceitouTermos,
                usuario = new
                {
                    usuario.Id,
                    usuario.Nome,
                    usuario.Email,
                    usuario.Nickname,
                    usuario.Descricao,
                    usuario.Avatar,
                    usuario.Tema,
                    usuario.Adm,
                    usuario.Ativo,
                    usuario.AceitouTermos
                }
            });
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning($"Token inválido: {ex.Message}");
            return Unauthorized(new { mensagem = "Token do Google inválido" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no login com Google");
            return StatusCode(500, new { mensagem = "Erro interno no login" });
        }
    }

    /// <summary>
    /// Login com Google (Authorization Code) — fluxo web/TWA
    /// </summary>
    [AllowAnonymous]
    [HttpPost("google-code")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> LoginComGoogleCode([FromBody] GoogleCodeRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.Code))
            return BadRequest(new { mensagem = "Código de autorização é obrigatório" });

        if (request.Code.Length > 5000)
            return BadRequest(new { mensagem = "Código inválido" });

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var redirectUri = request.RedirectUri ?? _configuration["Authentication:Google:RedirectUri"]!;

            // 🆕 Web Client (usado pra trocar code por token)
            var webClientId = _configuration["Authentication:Google:WebClientId"]
                              ?? _configuration["Authentication:Google:ClientId"]!;
            var webClientSecret = _configuration["Authentication:Google:WebClientSecret"]
                                  ?? _configuration["Authentication:Google:ClientSecret"]!;

            var tokenResponse = await httpClient.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = request.Code,
                    ["client_id"] = webClientId,
                    ["client_secret"] = webClientSecret,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code"
                }));

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var error = await tokenResponse.Content.ReadAsStringAsync();
                _logger.LogWarning($"Erro Google Token: {error}");
                return BadRequest(new { mensagem = "Erro ao validar código com o Google." });
            }

            var tokenData = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>();
            if (tokenData == null || string.IsNullOrEmpty(tokenData.IdToken))
                return BadRequest(new { mensagem = "Token do Google não recebido." });

            // 🆕 Valida o idToken contra o Web Client (mesmo usado na troca)
            var payload = await ValidarGoogleTokenAsync(tokenData.IdToken, webClientId);
            if (payload == null)
                return Unauthorized(new { mensagem = "Token do Google inválido" });

            var googleId = payload.Subject;
            var nome = payload.Name;
            var email = payload.Email;

            if (string.IsNullOrEmpty(email))
                return BadRequest(new { mensagem = "Email não fornecido pelo Google" });

            var usuarios = await _usuarioRepository.FindAsync(u => u.IdGoogle == googleId || u.Email == email);
            var usuario = usuarios.FirstOrDefault();
            bool novoUsuario = false;

            if (usuario == null)
            {
                novoUsuario = true;
                usuario = new Usuario
                {
                    Id = Guid.NewGuid().ToString(),
                    IdGoogle = googleId,
                    Nome = nome,
                    Email = email,
                    DataCriacao = DateTime.UtcNow,
                    DataAtualizacao = DateTime.UtcNow,
                    Ativo = true,
                    Adm = false,
                    AceitouTermos = false
                };
                await _usuarioRepository.AddAsync(usuario);
            }
            else
            {
                if (usuario.Ativo == false)
                {
                    _logger.LogWarning($"Tentativa de login com conta desativada: {email}");
                    return Unauthorized(new { mensagem = CONTA_DESATIVADA_MENSAGEM });
                }

                usuario.Nome = nome;
                usuario.IdGoogle ??= googleId;
                usuario.DataAtualizacao = DateTime.UtcNow;
            }

            var token = _tokenService.GerarToken(usuario.Id, usuario.Nome, usuario.Email, "google");
            var refreshToken = GerarRefreshTokenSeguro();

            usuario.RefreshToken = refreshToken;
            usuario.DataAtualizacao = DateTime.UtcNow;
            await _usuarioRepository.UpdateAsync(usuario.Id, usuario);

            _ = Task.Run(() => RegistrarAcessoAsync(usuario.Id));

            return Ok(new
            {
                token,
                refreshToken,
                novoUsuario = novoUsuario || !usuario.AceitouTermos,
                precisaAceitarTermos = !usuario.AceitouTermos,
                usuario = new
                {
                    usuario.Id,
                    usuario.Nome,
                    usuario.Email,
                    usuario.Nickname,
                    usuario.Descricao,
                    usuario.Avatar,
                    usuario.Tema,
                    usuario.Adm,
                    usuario.Ativo,
                    usuario.AceitouTermos
                }
            });
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning($"Token inválido: {ex.Message}");
            return Unauthorized(new { mensagem = "Token inválido" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no login com Google Code");
            return StatusCode(500, new { mensagem = "Erro interno no login" });
        }
    }

    /// <summary>
    /// Refresh token
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.RefreshToken))
            return BadRequest(new { mensagem = "Refresh token é obrigatório" });

        if (request.RefreshToken.Length > 1000)
            return BadRequest(new { mensagem = "Refresh token inválido" });

        if (request.RefreshToken == "undefined" || request.RefreshToken == "null")
        {
            _logger.LogWarning("Refresh token inválido recebido (literal 'undefined'/'null')");
            return Unauthorized(new { mensagem = "Refresh token inválido" });
        }

        var usuarios = await _usuarioRepository.FindAsync(u => u.RefreshToken == request.RefreshToken);
        var usuario = usuarios.FirstOrDefault();

        if (usuario == null)
        {
            _logger.LogWarning("Tentativa de refresh com token inválido");
            return Unauthorized(new { mensagem = "Refresh token inválido" });
        }

        if (!usuario.Ativo)
            return Unauthorized(new { mensagem = CONTA_DESATIVADA_MENSAGEM });

        var novoAccessToken = _tokenService.GerarToken(usuario.Id, usuario.Nome, usuario.Email, "refresh");
        var novoRefreshToken = GerarRefreshTokenSeguro();

        usuario.RefreshToken = novoRefreshToken;
        usuario.DataAtualizacao = DateTime.UtcNow;
        await _usuarioRepository.UpdateAsync(usuario.Id, usuario);

        return Ok(new
        {
            token = novoAccessToken,
            refreshToken = novoRefreshToken,
            usuario = new
            {
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.Nickname,
                usuario.Descricao,
                usuario.Avatar,
                usuario.Tema,
                usuario.Adm,
                usuario.Ativo,
                usuario.AceitouTermos
            }
        });
    }

    /// <summary>
    /// Logout (invalida refresh token)
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    [EnableRateLimiting("GeneralPolicy")]
    public async Task<IActionResult> Logout()
    {
        var usuarioId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(usuarioId))
            return Unauthorized();

        var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario != null)
        {
            usuario.RefreshToken = null;
            usuario.DataAtualizacao = DateTime.UtcNow;
            await _usuarioRepository.UpdateAsync(usuarioId, usuario);
        }

        return Ok(new { mensagem = "Logout realizado com sucesso" });
    }

    // ============ MÉTODOS AUXILIARES ============

    /// <summary>
    /// Valida o idToken do Google.
    /// </summary>
    /// <param name="idToken">Token JWT do Google</param>
    /// <param name="audience">
    /// Client ID esperado como audience. Se null, usa o ClientId padrão (Android).
    /// No fluxo web (/google-code), deve-se passar o WebClientId.
    /// </param>
    private async Task<GoogleJsonWebSignature.Payload?> ValidarGoogleTokenAsync(string idToken, string? audience = null)
    {
        try
        {
            var clientId = audience ?? _configuration["Authentication:Google:ClientId"];
            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogError("Google ClientId não configurado");
                return null;
            }

            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            };

            return await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Erro ao validar token Google: {ex.Message}");
            return null;
        }
    }

    private string GerarRefreshTokenSeguro()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private async Task RegistrarAcessoAsync(string usuarioId)
    {
        try
        {
            var acesso = new Acesso
            {
                Id = Guid.NewGuid().ToString(),
                IdUsuario = usuarioId,
                DataCriacao = DateTime.UtcNow
            };
            await _acessoRepository.AddAsync(acesso);

            var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
            if (usuario != null)
            {
                usuario.UltimoAcesso = DateTime.UtcNow;
                usuario.TotalAcessos = (usuario.TotalAcessos) + 1;
                await _usuarioRepository.UpdateAsync(usuarioId, usuario);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erro ao registrar acesso para usuário {usuarioId}");
        }
    }
}