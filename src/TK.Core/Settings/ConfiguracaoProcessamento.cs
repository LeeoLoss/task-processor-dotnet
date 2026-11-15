namespace TK.Core.Settings;

// Configuração para processamento de tarefas
public class ConfiguracaoProcessamento
{
    // Definição do máximo de tentativas para processamento de tarefas
    public int MaximoTentativas { get; set; } = 3;
    
    // Segundos entre tentativas de reprocessamento
    public int SegundosEntreTentativas { get; set; } = 5;
}