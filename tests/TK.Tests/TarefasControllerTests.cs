using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TK.Api.Controllers;
using TK.Api.Dtos;
using TK.Core.Entities;
using TK.Core.Enums;
using TK.Tests.Fakes;

namespace TK.Tests;

public class TarefasControllerTests
{
    private readonly RepositorioFalso _repositorio = new();
    private readonly PublicadorFalso _publicador = new();

    private TarefasController CriarController() => new(_repositorio, _publicador);

    // POST /tarefas
    [Fact]
    public async Task Criar_salva_a_tarefa_como_Pendente_e_publica_na_fila()
    {
        var requisicao = new CriarTarefaRequisicao
        {
            Tipo = "  EnviarEmail ",
            Dados = JsonDocument.Parse("{\"para\": \"teste@exemplo.com\"}").RootElement.Clone()
        };

        var resultado = await CriarController().CriarTarefa(requisicao);

        // 202 Accepted: a tarefa foi aceita, mas será processada depois
        var aceita = Assert.IsType<AcceptedAtActionResult>(resultado);
        var resposta = Assert.IsType<TarefaResposta>(aceita.Value);
        Assert.Equal("EnviarEmail", resposta.Tipo);
        Assert.Equal(StatusTarefa.Pendente, resposta.Status);

        var salva = _repositorio.Obter(resposta.Id)!;
        Assert.Equal("EnviarEmail", salva.Tipo);
        Assert.Equal(StatusTarefa.Pendente, salva.Status);
        Assert.Contains("teste@exemplo.com", salva.Dados);

        Assert.Equal(resposta.Id, Assert.Single(_publicador.Publicados));
    }

    [Fact]
    public async Task Criar_sem_dados_guarda_um_JSON_vazio()
    {
        var requisicao = new CriarTarefaRequisicao { Tipo = "GerarRelatorio" };

        var resultado = await CriarController().CriarTarefa(requisicao);

        var aceita = Assert.IsType<AcceptedAtActionResult>(resultado);
        var resposta = Assert.IsType<TarefaResposta>(aceita.Value);
        Assert.Equal("{}", _repositorio.Obter(resposta.Id)!.Dados);
    }

    // GET /tarefas/{id}
    [Fact]
    public async Task ObterPorId_devolve_a_tarefa_quando_ela_existe()
    {
        var tarefa = new Tarefa { Tipo = "EnviarEmail", Dados = "{}" };
        await _repositorio.CriarAsync(tarefa);

        var resultado = await CriarController().ObterPorId(tarefa.Id);

        var ok = Assert.IsType<OkObjectResult>(resultado);
        var resposta = Assert.IsType<TarefaResposta>(ok.Value);
        Assert.Equal(tarefa.Id, resposta.Id);
        Assert.Equal(StatusTarefa.Pendente, resposta.Status);
    }

    // GET /tarefas/{id}
    [Fact]
    public async Task ObterPorId_devolve_404_quando_a_tarefa_nao_existe()
    {
        var resultado = await CriarController().ObterPorId(Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(resultado);
    }
}