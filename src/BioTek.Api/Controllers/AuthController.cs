using BioTek.Api.Models;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Infrastructure.Data;
using BioTek.Infrastructure.Repositories;
using BioTek.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;

namespace BioTek.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IRepository<Usuario> _usuarioRepository;
    private readonly TokenService _tokenService;
    private readonly IConfiguration _configuration;
    private const string CONTA_DESATIVADA_MENSAGEM = "Conta desativada. Entre em contato com os administradores pelo email: g.britoalves888@gmail.com";

    private readonly IRepository<Acesso> _acessoRepository;
    public AuthController(MongoDbContext context, TokenService tokenService, IConfiguration configuration)
    {
        _usuarioRepository = new MongoRepository<Usuario>(context, "Usuarios");
        _acessoRepository = new MongoRepository<Acesso>(context, "Acessos"); 
        _tokenService = tokenService;
        _configuration = configuration;

    }

    [HttpPost("mock")]
    public async Task<IActionResult> LoginMock([FromBody] SocialLoginRequest request)
    {
        if (string.IsNullOrEmpty(request.Nome) || string.IsNullOrEmpty(request.Email))
            return BadRequest(new { mensagem = "Nome e Email são obrigatórios" });

        var googleId = "mock-" + Guid.NewGuid().ToString("N")[..8];
        var nome = request.Nome;
        var email = request.Email;

        var usuarios = await _usuarioRepository.FindAsync(u => u.Email == email);
        var usuario = usuarios.FirstOrDefault();
        bool novoUsuario = false;

        if (usuario == null)
        {
            novoUsuario = true;
            usuario = new Usuario
            {
                IdGoogle = googleId,
                Nome = nome,
                Email = email,
                Nickname = request.Nickname ?? ""
            };
            await _usuarioRepository.AddAsync(usuario);
        }
        else
        {
            usuario.Nome = nome;
            usuario.DataAtualizacao = DateTime.UtcNow;
            await _usuarioRepository.UpdateAsync(usuario.Id, usuario);

            // 🔴 Verificar conta desativada
            if (usuario.Ativo == false)
                return Unauthorized(new { mensagem = CONTA_DESATIVADA_MENSAGEM });
        }

        var token = _tokenService.GerarToken(usuario.Id, usuario.Nome, usuario.Email, "mock");

        await RegistrarAcesso(usuario.Id);

        return Ok(new
        {
            token,
            novoUsuario = novoUsuario || !usuario.AceitouTermos,
            precisaAceitarTermos = !usuario.AceitouTermos,
            usuario = new
            {
                usuario.Id, usuario.Nome, usuario.Email, usuario.Nickname,
                usuario.Descricao, usuario.Avatar, usuario.Tema,
                usuario.Adm, usuario.Ativo, usuario.AceitouTermos
            }
        });
    }

    [HttpPost("google")]
    public async Task<IActionResult> LoginComGoogle([FromBody] SocialLoginRequest request)
    {
        if (string.IsNullOrEmpty(request.IdToken))
            return BadRequest(new { mensagem = "Token do Google é obrigatório" });

        try
        {
            var clientId = _configuration["Authentication:Google:ClientId"]!;
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);

            var googleId = payload.Subject;
            var nome = payload.Name;
            var email = payload.Email;

            var usuarios = await _usuarioRepository.FindAsync(u => u.IdGoogle == googleId || u.Email == email);
            var usuario = usuarios.FirstOrDefault();
            bool novoUsuario = false;

            if (usuario == null)
            {
                novoUsuario = true;
                usuario = new Usuario
                {
                    IdGoogle = googleId,
                    Nome = nome,
                    Email = email
                };
                await _usuarioRepository.AddAsync(usuario);
            }
            else
            {
                // 🔴 Verificar conta desativada ANTES de atualizar
                if (usuario.Ativo == false)
                    return Unauthorized(new { mensagem = CONTA_DESATIVADA_MENSAGEM });

                usuario.Nome = nome;
                usuario.IdGoogle ??= googleId;
                usuario.DataAtualizacao = DateTime.UtcNow;
                await _usuarioRepository.UpdateAsync(usuario.Id, usuario);
            }

            var token = _tokenService.GerarToken(usuario.Id, usuario.Nome, usuario.Email, "google");

            await RegistrarAcesso(usuario.Id);

            return Ok(new
            {
                token,
                novoUsuario = novoUsuario || !usuario.AceitouTermos,
                precisaAceitarTermos = !usuario.AceitouTermos,
                usuario = new
                {
                    usuario.Id, usuario.Nome, usuario.Email, usuario.Nickname,
                    usuario.Descricao, usuario.Avatar, usuario.Tema,
                    usuario.Adm, usuario.Ativo, usuario.AceitouTermos
                }
            });
        }
        catch (InvalidJwtException ex)
        {
            Console.WriteLine($"❌ Token inválido: {ex.Message}");
            return Unauthorized(new { mensagem = $"Token do Google inválido: {ex.Message}" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Erro: {ex.Message}");
            return StatusCode(500, new { mensagem = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpPost("google-code")]
    public async Task<IActionResult> LoginComGoogleCode([FromBody] GoogleCodeRequest request)
    {
        if (string.IsNullOrEmpty(request.Code))
            return BadRequest(new { mensagem = "Código de autorização é obrigatório" });

        try
        {
            using var httpClient = new HttpClient();
            var redirectUri = request.RedirectUri ?? _configuration["Authentication:Google:RedirectUri"]!;

            var tokenResponse = await httpClient.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = request.Code,
                    ["client_id"] = _configuration["Authentication:Google:ClientId"]!,
                    ["client_secret"] = _configuration["Authentication:Google:ClientSecret"]!,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code"
                }));

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var error = await tokenResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Erro Google Token: {error}");
                return BadRequest(new { mensagem = "Erro ao validar código com o Google." });
            }

            var tokenData = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>();
            if (tokenData == null || string.IsNullOrEmpty(tokenData.IdToken))
                return BadRequest(new { mensagem = "Token do Google não recebido." });

            var clientId = _configuration["Authentication:Google:ClientId"]!;
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            };
            var payload = await GoogleJsonWebSignature.ValidateAsync(tokenData.IdToken, settings);

            var googleId = payload.Subject;
            var nome = payload.Name;
            var email = payload.Email;

            var usuarios = await _usuarioRepository.FindAsync(u => u.IdGoogle == googleId || u.Email == email);
            var usuario = usuarios.FirstOrDefault();
            bool novoUsuario = false;

            if (usuario == null)
            {
                novoUsuario = true;
                usuario = new Usuario
                {
                    IdGoogle = googleId,
                    Nome = nome,
                    Email = email
                };
                usuario.DataAtualizacao = DateTime.UtcNow;
                await _usuarioRepository.AddAsync(usuario);
            }
            else
            {
                // 🔴 Verificar conta desativada ANTES de atualizar
                if (usuario.Ativo == false)
                    return Unauthorized(new { mensagem = CONTA_DESATIVADA_MENSAGEM });

                usuario.Nome = nome;
                usuario.IdGoogle ??= googleId;
                usuario.DataAtualizacao = DateTime.UtcNow;
                await _usuarioRepository.UpdateAsync(usuario.Id, usuario);
            }

            var token = _tokenService.GerarToken(usuario.Id, usuario.Nome, usuario.Email, "google");

            await RegistrarAcesso(usuario.Id);

            return Ok(new
            {
                token,
                novoUsuario = novoUsuario || !usuario.AceitouTermos,
                precisaAceitarTermos = !usuario.AceitouTermos,
                usuario = new
                {
                    usuario.Id, usuario.Nome, usuario.Email, usuario.Nickname,
                    usuario.Descricao, usuario.Avatar, usuario.Tema,
                    usuario.Adm, usuario.Ativo, usuario.AceitouTermos
                }
            });
        }
        catch (InvalidJwtException ex)
        {
            return Unauthorized(new { mensagem = $"Token inválido: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensagem = ex.Message });
        }
    }

    private async Task RegistrarAcesso(string usuarioId)
    {
        // Registrar na collection Acessos
        var acesso = new Acesso
        {
            Id = Guid.NewGuid().ToString(),
            IdUsuario = usuarioId,
            DataCriacao = DateTime.UtcNow
        };
        await _acessoRepository.AddAsync(acesso);

        // Atualizar Usuario
        var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario != null)
        {
            usuario.UltimoAcesso = DateTime.UtcNow;
            usuario.TotalAcessos = (usuario.TotalAcessos) + 1;
            await _usuarioRepository.UpdateAsync(usuarioId, usuario);
        }
    }
}