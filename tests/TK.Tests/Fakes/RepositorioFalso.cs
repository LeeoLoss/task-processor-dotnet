using TK.Core.Entities;
using TK.Core.Enums;
using TK.Core.Interfaces;

namespace TK.Tests.Fakes;

// Repositório em memória, para testar sem MongoDB.
// Segue as mesmas regras do repositório real, inclusive a reserva atômica
public class RepositorioFalso : ITarefaRepositorio
{
    // Trava para garantir que só uma thread por vez entre nos métodos, 
    // simulando a operação atômica do MongoDB
    private readonly object _trava = new();
    private readonly Dictionary<Guid, Tarefa> _tarefas = new();

    // Consulta síncrona, para os testes conferirem o que ficou guardado
    public Tarefa? Obter(Guid id)
    {
        lock (_trava)
        {
            return _tarefas.TryGetValue(id, out var tarefa) ? Copiar(tarefa) : null;
        }
    }

    public Task CriarAsync(Tarefa tarefa)
    {
        lock (_trava)
        {
            _tarefas[tarefa.Id] = Copiar(tarefa);
        }

        return Task.CompletedTask;
    }

    public Task<Tarefa?> ObterPorIdAsync(Guid id)
    {
        return Task.FromResult(Obter(id));
    }

    // Atualiza síncronamente, para os testes conferirem o que ficou guardado
    public Task AtualizarStatusAsync(Guid id, StatusTarefa status, string? mensagemErro = null)
    {
        lock (_trava)
        {
            if (_tarefas.TryGetValue(id, out var tarefa))
            {
                tarefa.Status = status;

                if (mensagemErro is not null)
                {
                    tarefa.MensagemErro = mensagemErro;
                }
            }
        }

        return Task.CompletedTask;
    }

    // Reserva atômica: se outro Worker já pegou a tarefa, volta null
    public Task<Tarefa?> ReservarParaProcessamentoAsync(Guid id, bool mensagemReentregue)
    {
        // O lock faz o papel da operação atômica do MongoDB: só uma chamada por vez entra aqui
        lock (_trava)
        {
            if (!_tarefas.TryGetValue(id, out var tarefa))
            {
                return Task.FromResult<Tarefa?>(null);
            }

            var podeReservar = tarefa.Status == StatusTarefa.Pendente
                               || (mensagemReentregue && tarefa.Status == StatusTarefa.EmProcessamento);

            if (!podeReservar)
            {
                return Task.FromResult<Tarefa?>(null);
            }

            tarefa.Status = StatusTarefa.EmProcessamento;
            tarefa.Tentativas++;

            return Task.FromResult<Tarefa?>(Copiar(tarefa));
        }
    }

    // Devolve uma cópia, como o banco faria, 
    // para os testes não alterarem o armazenado por engano
    private static Tarefa Copiar(Tarefa tarefa) => new()
    {
        Id = tarefa.Id,
        Tipo = tarefa.Tipo,
        Dados = tarefa.Dados,
        Status = tarefa.Status,
        Tentativas = tarefa.Tentativas,
        MensagemErro = tarefa.MensagemErro,
        CriadaEm = tarefa.CriadaEm
    };
}