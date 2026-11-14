using MongoDB.Driver;
using TK.Core.Entities;
using TK.Core.Enums;
using TK.Core.Interfaces;
using TK.Core.Settings;

namespace TK.Core.Repositories;

// Implementação do contrato ITarefaRepositorio usando o MongoDB
public class TarefaRepositorio : ITarefaRepositorio
{
    // Coleção de tarefas do banco
    private readonly IMongoCollection<Tarefa> _colecaoTarefas;

    public TarefaRepositorio(IMongoClient cliente, ConfiguracaoMongo configuracao)
    {
        var banco = cliente.GetDatabase(configuracao.NomeBanco);
        _colecaoTarefas = banco.GetCollection<Tarefa>(configuracao.NomeColecaoTarefas);
    }

    public Task CriarAsync(Tarefa tarefa)
    {
        return _colecaoTarefas.InsertOneAsync(tarefa);
    }

    public async Task<Tarefa?> ObterPorIdAsync(Guid id)
    {
        // Se o id tarefa for inexistente, retornará null
        return await _colecaoTarefas.Find(t => t.Id == id).FirstOrDefaultAsync();
    }

    public async Task AtualizarStatusAsync(Guid id, StatusTarefa status)
    {
        // Altera só o campo Status, sem reescrever o resto do documento
        var atualizacao = Builders<Tarefa>.Update.Set(t => t.Status, status);

        await _colecaoTarefas.UpdateOneAsync(t => t.Id == id, atualizacao);
    }
}