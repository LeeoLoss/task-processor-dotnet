using MongoDB.Driver;
using TK.Core.Entities;
using TK.Core.Interfaces;
using TK.Core.Settings;

namespace TK.Core.Repositories;

// Implementação do contrato ITarefaRepositorio usando MongoDB
public class TarefaRepositorio : ITarefaRepositorio
{
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
        return await _colecaoTarefas.Find(t => t.Id == id).FirstOrDefaultAsync();
    }
}