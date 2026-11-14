using MongoDB.Driver;
using TK.Core.Interfaces;
using TK.Core.Repositories;
using TK.Core.Settings;

var builder = WebApplication.CreateBuilder(args);

// Define como o MongoDB grava Guid e enums (precisa rodar antes de qualquer uso do banco)
MapeamentoMongo.Registrar();

// Carrega a configuração do MongoDB a partir do appsettings.json
var configuracaoMongo = builder.Configuration.GetSection("Mongo").Get<ConfiguracaoMongo>()
                        ?? new ConfiguracaoMongo();

// Conexão com o MongoDB
builder.Services.AddSingleton(configuracaoMongo);
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(configuracaoMongo.StringConexao));
builder.Services.AddSingleton<ITarefaRepositorio, TarefaRepositorio>();

// Controllers e Swagger
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()); 
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger ativo somente no desenvolvimento
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();