using MongoDB.Driver;
using TK.Core.Interfaces;
using TK.Core.Messaging;
using TK.Core.Repositories;
using TK.Core.Settings;

var builder = WebApplication.CreateBuilder(args);

// Define como o MongoDB grava Guid e enums (precisa rodar antes de qualquer uso do banco)
MapeamentoMongo.Registrar();

// Leitura da configuração "Mongo" do appsettings.json
var configuracaoMongo = builder.Configuration.GetSection("Mongo").Get<ConfiguracaoMongo>()
                        ?? new ConfiguracaoMongo();

// Singleton: a mesma instância é usada em toda a aplicação
builder.Services.AddSingleton(configuracaoMongo);
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(configuracaoMongo.StringConexao));
builder.Services.AddSingleton<ITarefaRepositorio, TarefaRepositorio>();

// Configuração e publicador do RabbitMQ
var configuracaoRabbitMq = builder.Configuration.GetSection("RabbitMq").Get<ConfiguracaoRabbitMq>()
                           ?? new ConfiguracaoRabbitMq();

builder.Services.AddSingleton(configuracaoRabbitMq);
builder.Services.AddSingleton<IPublicadorTarefa, PublicadorTarefa>();

// Controllers e Swagger
builder.Services.AddControllers()
    .AddJsonOptions(opcoes =>
        opcoes.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// O Swagger só fica ativo em desenvolvimento
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();