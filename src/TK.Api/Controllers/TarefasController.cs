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
    
    public TarefasController(ITarefaRepositorio repositorio)
    {
        _repositorio = repositorio;
    }

    // POST /tarefas: Criar uma nova tarefa com status "Pendente" e armazená-la no banco de dados
    [HttpPost]
    public async Task<IActionResult> CriarTarefa([FromBody] CriarTarefaRequisicao requisicao)
    {
        var tarefa = new Tarefa
        {
            Tipo = requisicao.Tipo.Trim(),
            Dados = requisicao.Dados?.GetRawText() ?? "{}"
        };

        await _repositorio.CriarAsync(tarefa);

        // Retorna 202 Accepted com a localização da nova tarefa criada
        return AcceptedAtAction(nameof(ObterPorId), new { id = tarefa.Id }, TarefaResposta.De(tarefa));
    }

    // GET /tarefas/{id}: Obter uma tarefa pelo seu ID
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