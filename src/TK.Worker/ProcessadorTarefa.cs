using System.Text.Json;
using TK.Core.Entities;
using TK.Core.Enums;
using TK.Core.Interfaces;
using TK.Core.Settings;

namespace TK.Worker;

// Processador de tarefas, que pega uma tarefa do banco, processa e atualiza o status no banco
public class ProcessadorTarefa
{
    private readonly ITarefaRepositorio _repositorio;
    private readonly IPublicadorTarefa _publicador;
    private readonly ConfiguracaoProcessamento _processamento;
    private readonly ILogger<ProcessadorTarefa> _logger;

    // Construtor com injeção de dependências
    public ProcessadorTarefa(
        ITarefaRepositorio repositorio,
        IPublicadorTarefa publicador,
        ConfiguracaoProcessamento processamento,
        ILogger<ProcessadorTarefa> logger)
    {
        _repositorio = repositorio;
        _publicador = publicador;
        _processamento = processamento;
        _logger = logger;
    }

    // Processa uma tarefa, retornando o resultado do processamento
    public async Task<ResultadoProcessamento> ProcessarAsync(
        Guid tarefaId,
        bool mensagemReentregue,
        CancellationToken ct)
    {
        // Reserva atômica: se outro Worker já pegou a tarefa, volta null
        var tarefa = await _repositorio.ReservarParaProcessamentoAsync(tarefaId, mensagemReentregue);

        // Se não conseguiu reservar, ignora a tarefa (já processada, em andamento em outro Worker ou inexistente)
        if (tarefa is null)
        {
            _logger.LogInformation(
                "Tarefa {TarefaId} não pôde ser reservada (já processada, em andamento em outro Worker ou inexistente). Ignorando.",
                tarefaId);
            return ResultadoProcessamento.Ignorada;
        }

        // Processa a tarefa
        _logger.LogInformation(
            "Processando tarefa {TarefaId} do tipo '{Tipo}' (tentativa {Tentativa}/{Maximo})...",
            tarefa.Id, tarefa.Tipo, tarefa.Tentativas, _processamento.MaximoTentativas);

        // Tenta executar a tarefa, tratando falhas e decidindo se deve tentar de novo ou desistir
        try
        {
            await ExecutarTarefaAsync(tarefa, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // O Worker está sendo desligado: não é uma falha da tarefa
            throw;
        }
        catch (Exception ex)
        {
            return await TratarFalhaAsync(tarefa, ex, ct);
        }

        // Se chegou aqui, a tarefa foi processada com sucesso: marca como concluída
        await _repositorio.AtualizarStatusAsync(tarefa.Id, StatusTarefa.Concluido);
        _logger.LogInformation("Tarefa {TarefaId} concluída.", tarefa.Id);

        return ResultadoProcessamento.Concluida;
    }

    // Decide o que fazer quando o processamento falha: tentar de novo ou desistir
    private async Task<ResultadoProcessamento> TratarFalhaAsync(Tarefa tarefa, Exception ex, CancellationToken ct)
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
            return ResultadoProcessamento.NovaTentativa;
        }

        _logger.LogError(
            "Tarefa {TarefaId} falhou após {Tentativas} tentativa(s) e foi marcada com Erro: {Erro}",
            tarefa.Id, tarefa.Tentativas, ex.Message);

        await _repositorio.AtualizarStatusAsync(tarefa.Id, StatusTarefa.Erro, ex.Message);
        return ResultadoProcessamento.Erro;
    }

    // Aqui entraria o trabalho de verdade (enviar e-mail, gerar relatório...).
    // O desafio não exige isso, então apenas uma demora de envio foi simulada
    private async Task ExecutarTarefaAsync(Tarefa tarefa, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(_processamento.SegundosProcessamento), ct);

        // Para testar a re-tentativa: com {"falharAte": 3} nos dados, a tarefa falha
        // enquanto o número da tentativa for menor que 3
        using var documento = JsonDocument.Parse(tarefa.Dados);

        // Se o JSON tiver a propriedade "falharAte" e o número da tentativa for menor que esse valor, simula uma falha
        if (documento.RootElement.ValueKind == JsonValueKind.Object
            && documento.RootElement.TryGetProperty("falharAte", out var valor)
            && valor.TryGetInt32(out var falharAte)
            && tarefa.Tentativas < falharAte)
        {
            throw new InvalidOperationException($"Falha simulada na tentativa {tarefa.Tentativas}.");
        }
    }
}