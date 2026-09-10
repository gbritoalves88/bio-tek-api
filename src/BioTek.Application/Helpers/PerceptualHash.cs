// BioTek.Application/Helpers/PerceptualHash.cs
using SkiaSharp;

namespace BioTek.Application.Helpers;

public static class PerceptualHash
{
    private const int TAMANHO_REDIMENSIONADO = 32;
    private const int TAMANHO_HASH = 8;
    private const int TOTAL_BITS = TAMANHO_HASH * TAMANHO_HASH;

    /// <summary>
    /// Gera hash perceptual (64 bits) - retorna long
    /// </summary>
    public static long Calcular(byte[] imageBytes)
    {
        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap == null)
            throw new InvalidOperationException("Não foi possível decodificar a imagem");
        
        return Calcular(bitmap);
    }

    /// <summary>
    /// Gera hash perceptual (64 bits) - retorna long
    /// </summary>
    public static long Calcular(SKBitmap bitmap)
    {
        using var pequena = Redimensionar(bitmap, TAMANHO_REDIMENSIONADO, TAMANHO_REDIMENSIONADO);
        var cinza = ConverterParaCinza(pequena);
        var dct = AplicarDCT2D(cinza);
        
        var bloco = new double[TAMANHO_HASH, TAMANHO_HASH];
        for (int y = 0; y < TAMANHO_HASH; y++)
            for (int x = 0; x < TAMANHO_HASH; x++)
                bloco[x, y] = dct[x, y];
        
        var valores = new List<double>();
        for (int y = 0; y < TAMANHO_HASH; y++)
            for (int x = 0; x < TAMANHO_HASH; x++)
                if (!(x == 0 && y == 0))
                    valores.Add(bloco[x, y]);
        
        var media = valores.Average();
        
        // 🆕 Usar ulong interno e converter no final
        ulong hash = 0;
        int bitIndex = 0;
        
        for (int y = 0; y < TAMANHO_HASH; y++)
        {
            for (int x = 0; x < TAMANHO_HASH; x++)
            {
                if (bloco[x, y] > media)
                    hash |= 1UL << bitIndex;
                
                bitIndex++;
            }
        }
        
        // 🆕 Converter para long (preserva bits)
        return unchecked((long)hash);
    }

    /// <summary>
    /// Similaridade entre dois hashes (0.0 a 1.0) - recebe long
    /// </summary>
    public static double Similaridade(long hash1, long hash2)
    {
        // 🆕 Converter para ulong para XOR correto
        ulong u1 = unchecked((ulong)hash1);
        ulong u2 = unchecked((ulong)hash2);
        
        ulong xor = u1 ^ u2;
        int bitsDiferentes = 0;
        
        while (xor > 0)
        {
            bitsDiferentes += (int)(xor & 1);
            xor >>= 1;
        }
        
        return 1.0 - ((double)bitsDiferentes / TOTAL_BITS);
    }

    // ============ MÉTODOS PRIVADOS ============

    private static SKBitmap Redimensionar(SKBitmap original, int largura, int altura)
    {
        var resultado = new SKBitmap(largura, altura, SKColorType.Rgba8888, SKAlphaType.Premul);
        
        using var canvas = new SKCanvas(resultado);
        using var paint = new SKPaint
        {
            IsAntialias = true
        };
        
        // 🆕 Usar overload mais recente com SKSamplingOptions
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawBitmap(original, new SKRect(0, 0, largura, altura), sampling, paint);
        
        return resultado;
    }

    private static double[,] ConverterParaCinza(SKBitmap bitmap)
    {
        var largura = bitmap.Width;
        var altura = bitmap.Height;
        var cinza = new double[largura, altura];
        
        for (int y = 0; y < altura; y++)
        {
            for (int x = 0; x < largura; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                cinza[x, y] = 0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue;
            }
        }
        
        return cinza;
    }

    private static double[,] AplicarDCT2D(double[,] input)
    {
        int n = input.GetLength(0);
        var output = new double[n, n];
        
        var cos = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                cos[i, j] = Math.Cos((2 * j + 1) * i * Math.PI / (2.0 * n));
        
        for (int u = 0; u < n; u++)
        {
            for (int v = 0; v < n; v++)
            {
                double soma = 0;
                
                for (int x = 0; x < n; x++)
                {
                    for (int y = 0; y < n; y++)
                    {
                        soma += input[x, y] * cos[u, x] * cos[v, y];
                    }
                }
                
                double cu = (u == 0) ? 1.0 / Math.Sqrt(2) : 1.0;
                double cv = (v == 0) ? 1.0 / Math.Sqrt(2) : 1.0;
                
                output[u, v] = 0.25 * cu * cv * soma;
            }
        }
        
        return output;
    }
}