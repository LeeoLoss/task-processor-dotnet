using Microsoft.AspNetCore.Mvc;
using TK.Api.Dtos;
using TK.Core.Entities;
using TK.Core.Interfaces;

namespace TK.Api.Controllers;

[ApiController]
[Route("tarefas")]
public class TarefasController : ControllerBase
{
    private readonly ITarefaRepositorio _repositorio;
    private readonly IPublicadorTarefa _publicador;

    public TarefasController(ITarefaRepositorio repositorio, IPublicadorTarefa publicador)
    {
        _repositorio = repositorio;
        _publicador = publicador;
    }

    // POST /tarefas: Criar uma nova tarefa com status "Pendente", armazená-la no banco de dados e enviá-la para a fila
    [HttpPost]
    public async Task<IActionResult> CriarTarefa([FromBody] CriarTarefaRequisicao requisicao)
    {
        var tarefa = new Tarefa
        {
            Tipo = requisicao.Tipo.Trim(),
            Dados = requisicao.Dados?.GetRawText() ?? "{}"
        };

        await _repositorio.CriarAsync(tarefa);

        // Avisa a fila de que existe uma tarefa nova para o Worker processar.
        // Vem depois de salvar, para a tarefa já existir no banco quando o Worker receber o Id
        await _publicador.PublicarAsync(tarefa.Id);

        // Retorna 202 Accepted com a localização da nova tarefa criada
        return AcceptedAtAction(nameof(ObterPorId), new { id = tarefa.Id }, TarefaResposta.De(tarefa));
    }

    // GET: Obter uma tarefa pelo seu ID
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id)
    {
        var tarefa = await _repositorio.ObterPorIdAsync(id);

        if (tarefa == null)
        {
            return NotFound(new { Mensagem = $"Tarefa com ID '{id}' não encontrada." });
        }

        return Ok(TarefaResposta.De(tarefa));
    }
}