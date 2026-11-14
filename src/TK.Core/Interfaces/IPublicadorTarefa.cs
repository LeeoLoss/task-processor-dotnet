namespace TK.Core.Interfaces;

// Publicador de tarefas
// Somente a API depende desta interface
public interface IPublicadorTarefa
{
    // Publica uma tarefa para ser processada
    Task PublicarAsync(Guid tarefaId);
}