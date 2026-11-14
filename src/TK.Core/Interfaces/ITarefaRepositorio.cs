using TK.Core.Entities;
using TK.Core.Enums;
namespace TK.Core.Interfaces;

// Contrato de acesso às tarefas no banco.
// A API e o Worker dependem desta interface, e não do MongoDB diretamente
public interface ITarefaRepositorio
{
    // Criar/Salvar uma nova tarefa
    Task CriarAsync(Tarefa tarefa);

    // Buscar tarefa via Id. Caso não exista, retorna null
    Task<Tarefa?> ObterPorIdAsync(Guid id);

    // Atualizar o status de uma tarefa 
    Task AtualizarStatusAsync(Guid id, StatusTarefa status);
}