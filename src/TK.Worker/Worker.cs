using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TK.Core.Enums;
using TK.Core.Interfaces;
using TK.Core.Settings;

namespace TK.Worker;

// Fica em segundo plano lendo a fila do RabbitMQ e processando uma tarefa por vez
public class Worker : BackgroundService
{
    private readonly ConfiguracaoRabbitMq _configuracao;
    private readonly ITarefaRepositorio _repositorio;
    private readonly ILogger<Worker> _logger;

    public Worker(
        ConfiguracaoRabbitMq configuracao,
        ITarefaRepositorio repositorio,
        ILogger<Worker> logger)
    {
        _configuracao = configuracao;
        _repositorio = repositorio;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fabrica = new ConnectionFactory
        {
            HostName = _configuracao.Host,
            Port = _configuracao.Porta,
            UserName = _configuracao.Usuario,
            Password = _configuracao.Senha
        };

        await using var conexao = await fabrica.CreateConnectionAsync(stoppingToken);
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: stoppingToken);

        // Cria a fila se ainda não existir
        await canal.QueueDeclareAsync(
            queue: _configuracao.NomeFilaTarefas,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // O RabbitMQ envia uma mensagem por vez para o Worker, 
        // que só recebe a próxima quando confirma que terminou de processar a anterior
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, evento) => ProcessarMensagemAsync(canal, evento, stoppingToken);

        // A mensagem só é removida da fila quando o Worker confirma que terminou de processá-la (BasicAckAsync)
        // Se o Worker cair no meio do processamento, o RabbitMQ devolve a mensagem para a fila
        await canal.BasicConsumeAsync(
            queue: _configuracao.NomeFilaTarefas,
            autoAck: false,
            consumer: consumidor,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Worker aguardando tarefas na fila '{Fila}'...", _configuracao.NomeFilaTarefas);

        try
        {
            // Worker fica ativo até o encerramento da aplicação
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker encerrando.");
        }
    }

    private async Task ProcessarMensagemAsync(IChannel canal, BasicDeliverEventArgs evento, CancellationToken ct)
    {
        // A mensagem é só o Id da tarefa, escrito como texto
        var texto = Encoding.UTF8.GetString(evento.Body.ToArray());

        if (!Guid.TryParse(texto, out var tarefaId))
        {
            // Mensagem inválida: não é um Guid. 
            // Apenas registra o erro e confirma a mensagem para que o RabbitMQ a remova da fila
            _logger.LogError("Mensagem inválida recebida: '{Mensagem}'. Descartada.", texto);
            await canal.BasicAckAsync(evento.DeliveryTag, multiple: false);
            return;
        }

        try
        {
            var tarefa = await _repositorio.ObterPorIdAsync(tarefaId);

            if (tarefa is null)
            {
                // Tarefa não encontrada no banco
                _logger.LogWarning("Tarefa {TarefaId} não encontrada no banco. Mensagem descartada.", tarefaId);
                await canal.BasicAckAsync(evento.DeliveryTag, multiple: false);
                return;
            }

            await _repositorio.AtualizarStatusAsync(tarefaId, StatusTarefa.EmProcessamento);
            _logger.LogInformation("Processando tarefa {TarefaId} do tipo '{Tipo}'...", tarefaId, tarefa.Tipo);

            // Aqui entraria o trabalho de verdade (enviar e-mail, gerar relatório...).
            // O desafio não exige isso, então apenas simulamos uma demora
            await Task.Delay(TimeSpan.FromSeconds(5), ct);

            await _repositorio.AtualizarStatusAsync(tarefaId, StatusTarefa.Concluido);
            _logger.LogInformation("Tarefa {TarefaId} concluída.", tarefaId);

            // Confirma a mensagem: agora o RabbitMQ pode removê-la da fila
            await canal.BasicAckAsync(evento.DeliveryTag, multiple: false);
        }
        catch (OperationCanceledException)
        {
            // Worker devolve a mensagem para a fila
            await canal.BasicNackAsync(evento.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            // Qualquer falha no processamento da tarefa é registrada 
            // e a mensagem é confirmada para não ficar em loop na fila
            _logger.LogError(ex, "Falha ao processar a tarefa {TarefaId}.", tarefaId);
            await _repositorio.AtualizarStatusAsync(tarefaId, StatusTarefa.Erro);
            await canal.BasicAckAsync(evento.DeliveryTag, multiple: false);
        }
    }
}