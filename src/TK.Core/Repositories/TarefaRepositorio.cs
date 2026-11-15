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

    // Construtor recebe o cliente do MongoDB e a configuração
    public TarefaRepositorio(IMongoClient cliente, ConfiguracaoMongo configuracao)
    {
        var banco = cliente.GetDatabase(configuracao.NomeBanco);
        _colecaoTarefas = banco.GetCollection<Tarefa>(configuracao.NomeColecaoTarefas);
    }

    // Insere uma nova tarefa no banco
    public Task CriarAsync(Tarefa tarefa)
    {
        return _colecaoTarefas.InsertOneAsync(tarefa);
    }

    // Busca uma tarefa pelo seu Id
    public async Task<Tarefa?> ObterPorIdAsync(Guid id)
    {
        // Retorna null quando nenhuma tarefa tem esse Id
        return await _colecaoTarefas.Find(t => t.Id == id).FirstOrDefaultAsync();
    }

    // Atualiza o status de uma tarefa, e opcionalmente a mensagem de erro
    public async Task AtualizarStatusAsync(Guid id, StatusTarefa status, string? mensagemErro = null)
    {
        // Altera só os campos necessários, sem reescrever o resto do documento
        var atualizacao = Builders<Tarefa>.Update.Set(t => t.Status, status);

        if (mensagemErro is not null)
        {
            atualizacao = atualizacao.Set(t => t.MensagemErro, mensagemErro);
        }

        await _colecaoTarefas.UpdateOneAsync(t => t.Id == id, atualizacao);
    }

    // Tenta reservar uma tarefa para processamento, mudando seu status para EmProcessamento
    // Retorna a tarefa já atualizada, ou null se não conseguiu reservar
    public async Task<Tarefa?> ReservarParaProcessamentoAsync(Guid id, bool mensagemReentregue)
    {
        // Em condições normais, só uma tarefa Pendente pode ser reservada.
        // Se a mensagem foi reentregue, aceita também uma que ficou presa em EmProcessamento
        var statusPermitidos = mensagemReentregue
            ? new[] { StatusTarefa.Pendente, StatusTarefa.EmProcessamento }
            : new[] { StatusTarefa.Pendente };

        // Filtra as tarefas com esse Id e com os status permitidos
        var filtro = Builders<Tarefa>.Filter.And(
            Builders<Tarefa>.Filter.Eq(t => t.Id, id),
            Builders<Tarefa>.Filter.In(t => t.Status, statusPermitidos));

        // Procura e atualiza numa única operação: é isso que impede dois Workers de pegarem a mesma tarefa
        var atualizacao = Builders<Tarefa>.Update
            .Set(t => t.Status, StatusTarefa.EmProcessamento)
            .Inc(t => t.Tentativas, 1);

        // Devolve o documento já atualizado, com o contador novo
        var opcoes = new FindOneAndUpdateOptions<Tarefa>
        {
            ReturnDocument = ReturnDocument.After
        };

        // Retorna null quando nenhuma tarefa tem esse Id
        return await _colecaoTarefas.FindOneAndUpdateAsync(filtro, atualizacao, opcoes);
    }
}