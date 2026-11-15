using Microsoft.Extensions.Logging.Abstractions;
using TK.Core.Entities;
using TK.Core.Enums;
using TK.Core.Settings;
using TK.Tests.Fakes;
using TK.Worker;

namespace TK.Tests;

public class ProcessadorTarefaTests
{
    private readonly RepositorioFalso _repositorio = new();
    private readonly PublicadorFalso _publicador = new();

    // Esperas zeradas, para os testes rodarem instantaneamente
    private ProcessadorTarefa CriarProcessador(int maximoTentativas = 3)
    {
        var configuracao = new ConfiguracaoProcessamento
        {
            MaximoTentativas = maximoTentativas,
            SegundosEntreTentativas = 0,
            SegundosProcessamento = 0
        };

        return new ProcessadorTarefa(_repositorio, _publicador, configuracao, NullLogger<ProcessadorTarefa>.Instance);
    }

    private async Task<Tarefa> CriarTarefaAsync(string dados = "{}")
    {
        var tarefa = new Tarefa { Tipo = "EnviarEmail", Dados = dados };
        await _repositorio.CriarAsync(tarefa);
        return tarefa;
    }

    [Fact]
    public async Task Conclui_a_tarefa_quando_o_processamento_funciona()
    {
        var tarefa = await CriarTarefaAsync();

        var resultado = await CriarProcessador().ProcessarAsync(tarefa.Id, false, CancellationToken.None);

        Assert.Equal(ResultadoProcessamento.Concluida, resultado);
        var salva = _repositorio.Obter(tarefa.Id)!;
        Assert.Equal(StatusTarefa.Concluido, salva.Status);
        Assert.Equal(1, salva.Tentativas);
        Assert.Empty(_publicador.Publicados);
    }

    [Fact]
    public async Task Recoloca_na_fila_quando_falha_e_ainda_ha_tentativas()
    {
        var tarefa = await CriarTarefaAsync("{\"falharAte\": 3}");

        var resultado = await CriarProcessador().ProcessarAsync(tarefa.Id, false, CancellationToken.None);

        Assert.Equal(ResultadoProcessamento.NovaTentativa, resultado);
        var salva = _repositorio.Obter(tarefa.Id)!;
        Assert.Equal(StatusTarefa.Pendente, salva.Status);
        Assert.Contains("Falha simulada", salva.MensagemErro);
        Assert.Equal(tarefa.Id, Assert.Single(_publicador.Publicados));
    }

    [Fact]
    public async Task Conclui_na_terceira_tentativa_quando_falha_duas_vezes()
    {
        var tarefa = await CriarTarefaAsync("{\"falharAte\": 3}");
        var processador = CriarProcessador();

        var primeira = await processador.ProcessarAsync(tarefa.Id, false, CancellationToken.None);
        var segunda = await processador.ProcessarAsync(tarefa.Id, false, CancellationToken.None);
        var terceira = await processador.ProcessarAsync(tarefa.Id, false, CancellationToken.None);

        Assert.Equal(ResultadoProcessamento.NovaTentativa, primeira);
        Assert.Equal(ResultadoProcessamento.NovaTentativa, segunda);
        Assert.Equal(ResultadoProcessamento.Concluida, terceira);
        var salva = _repositorio.Obter(tarefa.Id)!;
        Assert.Equal(StatusTarefa.Concluido, salva.Status);
        Assert.Equal(3, salva.Tentativas);
    }

    [Fact]
    public async Task Marca_erro_quando_esgota_as_tentativas()
    {
        var tarefa = await CriarTarefaAsync("{\"falharAte\": 99}");
        var processador = CriarProcessador(maximoTentativas: 3);

        await processador.ProcessarAsync(tarefa.Id, false, CancellationToken.None);
        await processador.ProcessarAsync(tarefa.Id, false, CancellationToken.None);
        var ultima = await processador.ProcessarAsync(tarefa.Id, false, CancellationToken.None);

        Assert.Equal(ResultadoProcessamento.Erro, ultima);
        var salva = _repositorio.Obter(tarefa.Id)!;
        Assert.Equal(StatusTarefa.Erro, salva.Status);
        Assert.Equal(3, salva.Tentativas);
        Assert.Contains("Falha simulada", salva.MensagemErro);

        // Recolocou na fila só nas duas primeiras falhas,
        // e desistiu na última, quando esgotou as tentativas
        Assert.Equal(2, _publicador.Publicados.Count);
    }

    [Fact]
    public async Task Ignora_uma_tarefa_que_nao_existe()
    {
        var resultado = await CriarProcessador().ProcessarAsync(Guid.NewGuid(), false, CancellationToken.None);

        Assert.Equal(ResultadoProcessamento.Ignorada, resultado);
    }

    [Fact]
    public async Task Ignora_uma_tarefa_que_ja_foi_concluida()
    {
        var tarefa = await CriarTarefaAsync();
        var processador = CriarProcessador();
        await processador.ProcessarAsync(tarefa.Id, false, CancellationToken.None);

        // Mensagem duplicada ou reentregue depois de a tarefa já ter terminado
        var resultado = await processador.ProcessarAsync(tarefa.Id, true, CancellationToken.None);

        Assert.Equal(ResultadoProcessamento.Ignorada, resultado);
        Assert.Equal(1, _repositorio.Obter(tarefa.Id)!.Tentativas);
    }

    [Fact]
    public async Task Tarefa_presa_em_processamento_so_e_assumida_quando_a_mensagem_foi_reentregue()
    {
        var tarefa = await CriarTarefaAsync();

        // Simula um Worker que reservou a tarefa e caiu antes de terminar
        await _repositorio.ReservarParaProcessamentoAsync(tarefa.Id, false);
        var processador = CriarProcessador();

        var entregaNormal = await processador.ProcessarAsync(tarefa.Id, false, CancellationToken.None);
        var reentrega = await processador.ProcessarAsync(tarefa.Id, true, CancellationToken.None);

        Assert.Equal(ResultadoProcessamento.Ignorada, entregaNormal);
        Assert.Equal(ResultadoProcessamento.Concluida, reentrega);
    }

    [Fact]
    public async Task Apenas_um_Worker_processa_a_tarefa_quando_varios_tentam_ao_mesmo_tempo()
    {
        var tarefa = await CriarTarefaAsync();
        var processador = CriarProcessador();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => processador.ProcessarAsync(tarefa.Id, false, CancellationToken.None))));

        Assert.Equal(1, resultados.Count(r => r == ResultadoProcessamento.Concluida));
        Assert.Equal(9, resultados.Count(r => r == ResultadoProcessamento.Ignorada));
        Assert.Equal(1, _repositorio.Obter(tarefa.Id)!.Tentativas);
    }
    
    [Fact]
    public async Task Propaga_o_cancelamento_sem_tratar_como_falha_da_tarefa()
    {
        var tarefa = await CriarTarefaAsync();
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CriarProcessador().ProcessarAsync(tarefa.Id, false, cancelamento.Token));

        // Não virou erro nem foi recolocada na fila, 
        // o RabbitMQ devolve a mensagem e outro Worker assume
        Assert.Equal(StatusTarefa.EmProcessamento, _repositorio.Obter(tarefa.Id)!.Status);
        Assert.Empty(_publicador.Publicados);
    }
}