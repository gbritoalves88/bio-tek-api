using System.Text;
using BioTek.Application.Services;
using BioTek.Domain.Entities;
using BioTek.Domain.Interfaces;
using BioTek.Infrastructure.Data;
using BioTek.Infrastructure.Repositories;
using BioTek.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
// 2. SERVIÇOS DE INFRAESTRUTURA
// ==========================================

// Token JWT
builder.Services.AddSingleton<TokenService>();

// MongoDB (Singleton = conexão única)
builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("MongoDb");
    return new MongoDbContext(connectionString!, "BioTek");
});

// ==========================================
// 3. REPOSITÓRIOS (acesso a dados)
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

// Comentario
builder.Services.AddScoped<IRepository<ComentarioLivro>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<ComentarioLivro>(context, "ComentarioLivro");
});

//Coleção
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


builder.Services.AddScoped<IRepository<Catalogo>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<Catalogo>(context, "Catalogo");
});

builder.Services.AddScoped<IRepository<Notificacao>>(sp =>
{
    var context = sp.GetRequiredService<MongoDbContext>();
    return new MongoRepository<Notificacao>(context, "Notificacoes");
});



// ==========================================
// 4. SERVICES (regras de negócio)
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


// ==========================================
// 5. CONTROLLERS + SWAGGER
// ==========================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "https://biotek-three.vercel.app",
                "https://bliteca-three.vercel.app"
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
    // Configuração do botão Authorize no Swagger
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
// 6. PIPELINE HTTP
// ==========================================

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors("AllowAngular");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();