using System.Text;
using RabbitMQ.Client;
using TK.Core.Interfaces;
using TK.Core.Settings;

namespace TK.Core.Messaging;

// Envia o Id da tarefa para a fila do RabbitMQ
public class PublicadorTarefa : IPublicadorTarefa, IAsyncDisposable
{
    private readonly ConfiguracaoRabbitMq _configuracao;

    // Criação do canal e da conexão na primeira publicação e reaproveitados nas seguintes
    private IConnection? _conexao;
    private IChannel? _canal;

    // O canal do RabbitMQ não aceita duas publicações ao mesmo tempo.
    private readonly SemaphoreSlim _trava = new(1, 1);

    public PublicadorTarefa(ConfiguracaoRabbitMq configuracao)
    {
        _configuracao = configuracao;
    }

    public async Task PublicarAsync(Guid tarefaId)
    {
        await _trava.WaitAsync();
        try
        {
            var canal = await ObterCanalAsync();

            // A mensagem é só o Id da tarefa, escrito como texto
            var corpo = Encoding.UTF8.GetBytes(tarefaId.ToString());

            // Persistent: o RabbitMQ grava a mensagem em disco, então ela sobrevive a uma reinicialização
            var propriedades = new BasicProperties { Persistent = true };

            await canal.BasicPublishAsync(
                exchange: string.Empty, 
                routingKey: _configuracao.NomeFilaTarefas,
                mandatory: false,
                basicProperties: propriedades,
                body: corpo);
        }
        finally
        {
            _trava.Release();
        }
    }

    private async Task<IChannel> ObterCanalAsync()
    {
        // Em caso de existência do canal, este será reaproveitado. 
        // Caso contrário, será criada uma nova conexão e canal.
        if (_canal is { IsOpen: true })
        {
            return _canal;
        }

        var fabrica = new ConnectionFactory
        {
            HostName = _configuracao.Host,
            Port = _configuracao.Porta,
            UserName = _configuracao.Usuario,
            Password = _configuracao.Senha
        };

        _conexao = await fabrica.CreateConnectionAsync();
        _canal = await _conexao.CreateChannelAsync();

        // Cria a fila se ainda não existir. Pode ser chamado várias vezes sem problema.
        // A fila continuará existindo se o RabbitMQ reiniciar
        await _canal.QueueDeclareAsync(
            queue: _configuracao.NomeFilaTarefas,
            durable: true,
            exclusive: false,
            autoDelete: false);

        return _canal;
    }

    public async ValueTask DisposeAsync()
    {
        if (_canal is not null)
        {
            await _canal.CloseAsync();
        }

        if (_conexao is not null)
        {
            await _conexao.CloseAsync();
        }

        _trava.Dispose();
    }
}