using Amazon.S3;
using Amazon.S3.Model;
using BioTek.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BioTek.Infrastructure.Services;

public class R2Service : IR2Service
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;
    private readonly string _publicUrl;
    private readonly ILogger<R2Service> _logger;

    public R2Service(IConfiguration configuration, ILogger<R2Service> logger)
    {
        _logger = logger;
        _bucketName = configuration["R2:BucketName"]!;
        _publicUrl = configuration["R2:PublicUrl"]!;

        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{configuration["R2:AccountId"]}.r2.cloudflarestorage.com",
            ForcePathStyle = true
        };

        _s3 = new AmazonS3Client(
            configuration["R2:AccessKey"],
            configuration["R2:SecretKey"],
            config
        );
    }

    public async Task<string> UploadAsync(Stream stream, string extensao, string contentType)
    {
        try
        {
            var nomeArquivo = $"{Guid.NewGuid()}{extensao}";
            var chave = $"uploads/{nomeArquivo}";

            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = chave,
                InputStream = stream,
                ContentType = contentType,
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true
            };

            request.Headers.CacheControl = "public, max-age=31536000, immutable";

            await _s3.PutObjectAsync(request);

            var url = $"{_publicUrl}/{chave}";
            _logger.LogInformation($"Upload R2 OK: {url}");

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao fazer upload pro R2");
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string chave)
    {
        try
        {
            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = chave
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erro ao deletar {chave} do R2");
            return false;
        }
    }
}