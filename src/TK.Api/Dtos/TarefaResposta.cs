using System.Text.Json;
using TK.Core.Enums;
using TK.Core.Entities;

namespace TK.Api.Dtos;

// Devolução de dados da tarefa para o cliente (API)
public record TarefaResposta
    (
    Guid Id,
    string Tipo,
    JsonElement Dados,
    StatusTarefa Status,
    int Tentativas,
    string? MensagemErro,
    DateTime CriadaEm
    )
{
    // Converte a entidade (banco de dados) para o DTO (resposta da API)
    public static TarefaResposta De(Tarefa tarefa)
    {
        using var documento = JsonDocument.Parse(tarefa.Dados);

        return new TarefaResposta
        (
            tarefa.Id,
            tarefa.Tipo,
            documento.RootElement.Clone(),            
            tarefa.Status,
            tarefa.Tentativas,
            tarefa.MensagemErro,
            tarefa.CriadaEm
        );
    }
}