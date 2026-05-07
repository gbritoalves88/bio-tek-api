using BioTek.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// 🔹 Habilita controllers (para criar endpoints REST)
builder.Services.AddControllers();

// 🔹 Documentação Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 🔹 Conexão com MongoDB (Singleton = uma instância reutilizada)
builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("MongoDb");
    return new MongoDbContext(connectionString!, "BioTek");
});

var app = builder.Build();

// 🔹 Pipeline HTTP
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

// 🔹 Mapeia as rotas dos controllers
app.MapControllers();

app.Run();