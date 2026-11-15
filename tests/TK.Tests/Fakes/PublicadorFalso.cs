using TK.Core.Interfaces;

namespace TK.Tests.Fakes;

// Publicador que só anota os Ids, sem usar o RabbitMQ
public class PublicadorFalso : IPublicadorTarefa
{
    public List<Guid> Publicados { get; } = new();

    public Task PublicarAsync(Guid tarefaId)
    {
        lock (Publicados)
        {
            Publicados.Add(tarefaId);
        }

        return Task.CompletedTask;
    }
}