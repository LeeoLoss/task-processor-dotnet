using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace TK.Api.Dtos;

public class CriarTarefaRequisicao
{
    // Validação de dados da requisição para criar uma nova tarefa
    [Required(ErrorMessage = "O campo 'Tipo' é obrigatório.")]
    [StringLength(100, ErrorMessage = "O campo 'Tipo' deve ter no máximo 100 caracteres.")]
    public string Tipo { get; set; } = string.Empty;

    // Dados da tarefa em formato JSON, que podem variar dependendo do tipo de tarefa
    public JsonElement? Dados { get; set; }
}