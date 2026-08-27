using System.Text;
using System.Threading.RateLimiting;
using BioTek.Application.Services;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Infrastructure.Data;
using BioTek.Infrastructure.Repositories;
using BioTek.Infrastructure.Services;
using BioTek.Models;
using BioTek.Services;
using BioTek.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. CONFIGURAÇÃO JWT (Autenticação)
// ==========================================
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

builder.Services.AddAuthorization();

// ==========================================
// 2. RATE LIMITING (Proteção contra abuso)
// ==========================================

builder.Services.AddRateLimiter(options =>
{
    // Política para endpoints de notificação (mais restritiva)
    options.AddPolicy("NotificationPolicy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 30,           // 30 requisições
                Window = TimeSpan.FromMinutes(1),  // por minuto
                QueueLimit = 0               // sem fila (rejeita imediatamente)
            }));

    // Política geral para toda a API
    options.AddPolicy("GeneralPolicy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,          // 100 requisições
                Window = TimeSpan.FromMinutes(1),  // por minuto
                QueueLimit = 0
            }));

    // Configuração global
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? 
                         httpContext.Connection.RemoteIpAddress?.ToString() ?? 
                         "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Configuração de resposta quando exceder o limite
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        
        var response = new
        {
            message = "Muitas requisições. Tente novamente mais tarde.",
            retryAfter = 60 // segundos
        };
        
        await context.HttpContext.Response.WriteAsJsonAsync(response, token);
    };
});

// ==========================================
// 3. SERVIÇOS DE INFRAESTRUTURA
// ==========================================

// Token JWT
builder.Services.AddSingleton<TokenService>();

// Moderação de comentários (Singleton = lista estática)
builder.Services.AddSingleton<ModeracaoService>();

// MongoDB (Singleton = conexão única)
builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("MongoDb");
    return new MongoDbContext(connectionString!, "BioTek");
});

// ==========================================
// 4. REPOSITÓRIOS (acesso a dados)
// ==========================================

// Usuario
builder.Services.AddScoped<IRepository<Usuario>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<Usuario>(context, "Usuarios");
});

// Livro
builder.Services.AddScoped<IRepository<Livro>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<Livro>(context, "Livros");
});

// AvaliacaoLivro
builder.Services.AddScoped<IRepository<AvaliacaoLivro>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<AvaliacaoLivro>(context, "AvaliacoesLivros");
});

// Seguidor
builder.Services.AddScoped<IRepository<Seguidor>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<Seguidor>(context, "Seguidores");
});

// ComentarioLivro
builder.Services.AddScoped<IRepository<ComentarioLivro>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<ComentarioLivro>(context, "ComentarioLivro");
});

// Colecao
builder.Services.AddScoped<IRepository<Colecao>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<Colecao>(context, "Colecao");
});

// AvaliacaoColecao
builder.Services.AddScoped<IRepository<AvaliacaoColecao>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<AvaliacaoColecao>(context, "AvaliacoesColecoes");
});

// ComentarioColecao
builder.Services.AddScoped<IRepository<ComentarioColecao>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<ComentarioColecao>(context, "ComentariosColecoes");
});

// Catalogo
builder.Services.AddScoped<IRepository<Catalogo>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<Catalogo>(context, "Catalogo");
});

// Notificacoes
builder.Services.AddScoped<IRepository<Notificacao>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<Notificacao>(context, "Notificacoes");
});

// Curtidas
builder.Services.AddScoped<IRepository<Curtida>>(sp =>
    new MongoRepository<Curtida>(sp.GetRequiredService<MongoDbContext>(), "Curtidas"));

// Acesso
builder.Services.AddScoped<IRepository<Acesso>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<Acesso>(context, "Acessos");
});

// NotificationSubscriptions (Push Notifications)
builder.Services.AddScoped<IRepository<NotificationSubscription>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<NotificationSubscription>(context, "NotificationSubscriptions");
});

// Firebase Settings
builder.Services.Configure<FirebaseSettings>(options =>
{
    options.ProjectId = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID") 
                        ?? builder.Configuration["Firebase:ProjectId"];
    
    var serviceAccountJson = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_JSON");
    
    if (!string.IsNullOrEmpty(serviceAccountJson))
    {
        options.ServiceAccountJson = serviceAccountJson;
    }
    else
    {
        options.ServiceAccountJson = builder.Configuration["Firebase:ServiceAccountJson"];
    }
});

builder.Services.AddScoped<IRepository<StreakUsuario>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<StreakUsuario>(context, "StreakUsuarios");
});


// ==========================================
// 5. SERVICES (regras de negócio)
// ==========================================

builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<ILivroService, LivroService>();
builder.Services.AddScoped<IAvaliacaoLivroService, AvaliacaoLivroService>();
builder.Services.AddScoped<ISeguidorService, SeguidorService>();
builder.Services.AddScoped<IComentarioLivroService, ComentarioLivroService>();
builder.Services.AddScoped<IColecaoService, ColecaoService>();
builder.Services.AddScoped<IComentarioColecaoService, ComentarioColecaoService>();
builder.Services.AddScoped<IAvaliacaoColecaoService, AvaliacaoColecaoService>();
builder.Services.AddScoped<IComunidadeService, ComunidadeService>();
builder.Services.AddScoped<ICatalogoService, CatalogoService>();
builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddScoped<INotificacaoService, NotificacaoService>();
builder.Services.AddHttpClient<IFirebaseNotificationService, FirebaseNotificationService>();
builder.Services.AddHostedService<ReadingReminderBackgroundService>();
builder.Services.AddScoped<IStreakService, StreakService>();
builder.Services.AddHostedService<StreakNotificationBackgroundService>();
// ==========================================
// 6. CONTROLLERS + SWAGGER
// ==========================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "https://biotek-three.vercel.app",
                "https://bliteca-three.vercel.app",
                "https://www.bliteca.com.br"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Insira o token JWT no formato: Bearer {seu-token}"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ==========================================
// 7. PIPELINE HTTP
// ==========================================

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors("AllowAngular");

// Rate Limiting (antes da autenticação)
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();