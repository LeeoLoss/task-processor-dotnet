namespace TK.Core.Settings;

// Configuração para conexão com o RabbitMQ
public class ConfiguracaoRabbitMq
{
    public string Host { get; set; } = "localhost";
    public int Porta { get; set; } = 5672;
    public string Usuario { get; set; } = "guest";
    public string Senha { get; set; } = "guest";

    // Nome da fila para envio de tarefas
    public string NomeFilaTarefas { get; set; } = "fila_tarefas";
}