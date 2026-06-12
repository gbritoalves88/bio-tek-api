using System.Security.Claims;
using BioTek.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTek.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UploadController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public UploadController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Upload de imagem via ImgBB (API Key protegida no backend)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Upload([FromBody] UploadImagemRequest request)
    {
        if (string.IsNullOrEmpty(request.Base64) || !request.Base64.StartsWith("data:"))
            return BadRequest(new { mensagem = "Imagem inválida" });

        try
        {
            var imagemLimpa = request.Base64.Split(',')[1];
            var apiKey = _configuration["ImgBB:imgbbApiKey"]!;
            var imgUrl = _configuration["ImgBB:imgbbUrl"]!;

            using var httpClient = new HttpClient();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["key"] = apiKey,
                ["image"] = imagemLimpa
            });

            var response = await httpClient.PostAsync(imgUrl, content);
            var resultJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return BadRequest(new { mensagem = $"Erro ImgBB: {resultJson}" });

            var resultObj = System.Text.Json.JsonDocument.Parse(resultJson);
            var url = resultObj.RootElement.GetProperty("data").GetProperty("url").GetString();

            return Ok(new { url });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensagem = ex.Message });
        }
    }
}