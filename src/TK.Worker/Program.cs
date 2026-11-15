using MongoDB.Driver;
using TK.Core.Interfaces;
using TK.Core.Repositories;
using TK.Core.Settings;
using TK.Worker;

var builder = Host.CreateApplicationBuilder(args);

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

// Worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();