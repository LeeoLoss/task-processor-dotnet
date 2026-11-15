using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TK.Core.Settings;

namespace TK.Worker;

// Fica em segundo plano lendo a fila do RabbitMQ e entregando cada tarefa ao ProcessadorTarefa
public class Worker : BackgroundService
{
    // Injeção de dependências
    private readonly ConfiguracaoRabbitMq _configuracao;
    private readonly ProcessadorTarefa _processador;
    private readonly ILogger<Worker> _logger;

    // Construtor com injeção de dependências
    public Worker(
        ConfiguracaoRabbitMq configuracao,
        ProcessadorTarefa processador,
        ILogger<Worker> logger)
    {
        _configuracao = configuracao;
        _processador = processador;
        _logger = logger;
    }

    // Mantém o Worker vivo, recebendo mensagens da fila e entregando para o ProcessadorTarefa
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fabrica = new ConnectionFactory
        {
            HostName = _configuracao.Host,
            Port = _configuracao.Porta,
            UserName = _configuracao.Usuario,
            Password = _configuracao.Senha
        };

        // Cria a conexão e o canal com o RabbitMQ, garantindo que a fila existe
        await using var conexao = await fabrica.CreateConnectionAsync(stoppingToken);
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: stoppingToken);

        // Garante que a fila existe (mesma declaração do publicador)
        await canal.QueueDeclareAsync(
            queue: _configuracao.NomeFilaTarefas,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Configura o canal para receber apenas uma mensagem por vez, evitando sobrecarga no Worker
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, evento) => ProcessarMensagemAsync(canal, evento, stoppingToken);

        // autoAck: false, então a mensagem só sai da fila quando confirmarmos (Ack) no final
        // Se o Worker cair no meio do processamento, o RabbitMQ devolve a mensagem para a fila
        await canal.BasicConsumeAsync(
            queue: _configuracao.NomeFilaTarefas,
            autoAck: false,
            consumer: consumidor,
            cancellationToken: stoppingToken);

        // Loga que o Worker está pronto para receber mensagens
        _logger.LogInformation("Worker aguardando tarefas na fila '{Fila}'...", _configuracao.NomeFilaTarefas);

        // Mantém o Worker vivo até a aplicação ser encerrada
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker encerrando.");
        }
    }

    // Processa uma mensagem recebida do RabbitMQ, entregando a tarefa para o ProcessadorTarefa
    private async Task ProcessarMensagemAsync(IChannel canal, BasicDeliverEventArgs evento, CancellationToken ct)
    {
        // A mensagem é só o Id da tarefa, escrito como texto
        var texto = Encoding.UTF8.GetString(evento.Body.ToArray());

        if (!Guid.TryParse(texto, out var tarefaId))
        {
            // Mensagem inválida nunca vai melhorar se for reenviada, então descartamos
            _logger.LogError("Mensagem inválida recebida: '{Mensagem}'. Descartada.", texto);
            await canal.BasicAckAsync(evento.DeliveryTag, multiple: false);
            return;
        }

        // Processa a tarefa
        try
        {
            await _processador.ProcessarAsync(tarefaId, evento.Redelivered, ct);

            // Confirma a mensagem: agora o RabbitMQ pode removê-la da fila
            await canal.BasicAckAsync(evento.DeliveryTag, multiple: false);
        }
        catch (OperationCanceledException)
        {
            // O Worker está sendo desligado: devolve a mensagem para a fila
            await canal.BasicNackAsync(evento.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            // Erro de infraestrutura (por exemplo, o MongoDB fora do ar): devolve a mensagem para a fila
            _logger.LogError(ex, "Erro inesperado ao processar a tarefa {TarefaId}. A mensagem voltará para a fila.", tarefaId);
            await Task.Delay(TimeSpan.FromSeconds(2));
            await canal.BasicNackAsync(evento.DeliveryTag, multiple: false, requeue: true);
        }
    }
}