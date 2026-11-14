namespace TK.Core.Settings;

// Configuração para conexão com o MongoDB
public class ConfiguracaoMongo
{
    public string StringConexao { get; set; } = "mongodb://localhost:27017";
    public string NomeBanco { get; set; } = "task-processor";
    public string NomeColecaoTarefas { get; set; } = "tarefas";
}