using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TK.Core.Entities;
using TK.Core.Enums;
using TK.Core.Interfaces;
using TK.Core.Settings;

namespace TK.Worker;

// Fica em segundo plano lendo a fila do RabbitMQ e processando uma tarefa por vez
public class Worker : BackgroundService
{
    private readonly ConfiguracaoRabbitMq _configuracao;
    private readonly ConfiguracaoProcessamento _processamento;
    private readonly ITarefaRepositorio _repositorio;
    private readonly IPublicadorTarefa _publicador;
    private readonly ILogger<Worker> _logger;

    // Injeta as configurações via construtor
    public Worker(
        ConfiguracaoRabbitMq configuracao,
        ConfiguracaoProcessamento processamento,
        ITarefaRepositorio repositorio,
        IPublicadorTarefa publicador,
        ILogger<Worker> logger)
    {
        _configuracao = configuracao;
        _processamento = processamento;
        _repositorio = repositorio;
        _publicador = publicador;
        _logger = logger;
    }

    // Executado quando o Worker iniciar
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fabrica = new ConnectionFactory
        {
            HostName = _configuracao.Host,
            Port = _configuracao.Porta,
            UserName = _configuracao.Usuario,
            Password = _configuracao.Senha
        };

        // Cria uma nova conexão e canal
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

        // Aguarda tarefas na fila
        _logger.LogInformation("Worker aguardando tarefas na fila '{Fila}'...", _configuracao.NomeFilaTarefas);

        // Worker fica ativo até o encerramento da aplicação
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker encerrando.");
        }
    }

    private async Task ProcessarMensagemAsync(IChannel canal, BasicDeliverEventArgs evento, CancellationToken ct)
    {
        // A mensagem é só o Id da tarefa, escrito como string
        var texto = Encoding.UTF8.GetString(evento.Body.ToArray());

        if (!Guid.TryParse(texto, out var tarefaId))
        {
            // Mensagem inválida: não é um Guid. 
            // Apenas registra o erro e confirma a mensagem para que o RabbitMQ a remova da fila
            _logger.LogError("Mensagem inválida recebida: '{Mensagem}'. Descartada.", texto);
            await canal.BasicAckAsync(evento.DeliveryTag, multiple: false);
            return;
        }
        
Tarefa? tarefa = null;

        try
        {
            // Reserva atômica: se outro Worker já pegou a tarefa, volta null
            tarefa = await _repositorio.ReservarParaProcessamentoAsync(tarefaId, evento.Redelivered);

            if (tarefa is null)
            {
                _logger.LogInformation(
                    "Tarefa {TarefaId} não pôde ser reservada (já processada, em andamento em outro Worker ou inexistente). Ignorando.",
                    tarefaId);
                await canal.BasicAckAsync(evento.DeliveryTag, multiple: false);
                return;
            }

            _logger.LogInformation(
                "Processando tarefa {TarefaId} do tipo '{Tipo}' (tentativa {Tentativa}/{Maximo})...",
                tarefa.Id, tarefa.Tipo, tarefa.Tentativas, _processamento.MaximoTentativas);

            await ExecutarTarefaAsync(tarefa, ct);

            await _repositorio.AtualizarStatusAsync(tarefa.Id, StatusTarefa.Concluido);
            _logger.LogInformation("Tarefa {TarefaId} concluída.", tarefa.Id);

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
            if (tarefa is null)
            {
                // Falhou antes de reservar (por exemplo, o MongoDB fora do ar): devolve a mensagem para a fila
                _logger.LogError(ex, "Erro ao reservar a tarefa {TarefaId}. A mensagem voltará para a fila.", tarefaId);
                await Task.Delay(TimeSpan.FromSeconds(2));
                await canal.BasicNackAsync(evento.DeliveryTag, multiple: false, requeue: true);
                return;
            }

            try
            {
                await TratarFalhaAsync(tarefa, ex, ct);
                await canal.BasicAckAsync(evento.DeliveryTag, multiple: false);
            }
            catch (OperationCanceledException)
            {
                // Desligamento durante a espera da re-tentativa: a mensagem volta para a fila
                await canal.BasicNackAsync(evento.DeliveryTag, multiple: false, requeue: true);
            }
        }
    }

    // Decide o que fazer quando o processamento falha: tentar de novo ou desistir
    private async Task TratarFalhaAsync(Tarefa tarefa, Exception ex, CancellationToken ct)
    {
        if (tarefa.Tentativas < _processamento.MaximoTentativas)
        {
            _logger.LogWarning(
                "Falha na tentativa {Tentativa}/{Maximo} da tarefa {TarefaId}: {Erro}. Nova tentativa em {Segundos}s.",
                tarefa.Tentativas, _processamento.MaximoTentativas, tarefa.Id, ex.Message,
                _processamento.SegundosEntreTentativas);

            // Volta para Pendente, guardando o erro, para a tarefa poder ser reservada de novo
            await _repositorio.AtualizarStatusAsync(tarefa.Id, StatusTarefa.Pendente, ex.Message);

            // Espera um pouco antes de recolocar na fila, para não insistir imediatamente
            await Task.Delay(TimeSpan.FromSeconds(_processamento.SegundosEntreTentativas), ct);

            await _publicador.PublicarAsync(tarefa.Id);
            return;
        }

        _logger.LogError(
            "Tarefa {TarefaId} falhou após {Tentativas} tentativa(s) e foi marcada com Erro: {Erro}",
            tarefa.Id, tarefa.Tentativas, ex.Message);

        await _repositorio.AtualizarStatusAsync(tarefa.Id, StatusTarefa.Erro, ex.Message);
    }

    // Aqui entraria o trabalho de verdade (enviar e-mail, gerar relatório...).
    // O desafio não exige isso, então apenas simulamos uma demora
    private static async Task ExecutarTarefaAsync(Tarefa tarefa, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        // Para testar a re-tentativa:
        // a tarefa falha enquanto o número da tentativa for menor que 3
        using var documento = JsonDocument.Parse(tarefa.Dados);

        // Se o JSON tiver a propriedade "falharAte" com um número, falha até atingir esse número de tentativas
        if (documento.RootElement.ValueKind == JsonValueKind.Object
            && documento.RootElement.TryGetProperty("falharAte", out var valor)
            && valor.TryGetInt32(out var falharAte)
            && tarefa.Tentativas < falharAte)
        {
            throw new InvalidOperationException($"Falha simulada na tentativa {tarefa.Tentativas}.");
        }
    }
}