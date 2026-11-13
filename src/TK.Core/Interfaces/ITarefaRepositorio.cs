using TK.Core.Entities;

namespace TK.Core.Interfaces;

public interface ITarefaRepositorio
{
    // Criar/Salver uma nova tarefa
    Task CriarAsync(Tarefa tarefa);

    // Buscar tarefa via Id. Caso não exista, retorna null
    Task<Tarefa?> ObterPorIdAsync(Guid id);
}