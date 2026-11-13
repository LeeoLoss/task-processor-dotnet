using TK.Core.Enums;

namespace TK.Core.Entities;

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tipo { get; set; } = string.Empty;
    public string Dados { get; set; } = "{}";
    public StatusTarefa Status { get; set; } = StatusTarefa.Pendente;
    public DateTime CriadaEm { get; set; }
}