# Task Processor

![CI](https://github.com/LeeoLoss/task-processor-dotnet/actions/workflows/ci.yml/badge.svg)

Serviço de processamento de tarefas em segundo plano, feito em C# (.NET 8), desenvolvido como desafio técnico para a vaga de Desenvolvedor Jr C# / ASP.NET.

A API recebe tarefas, guarda no MongoDB e as coloca numa fila do RabbitMQ. Um ou mais Workers consomem a fila, processam as tarefas em segundo plano e mantêm o status atualizado, com re-tentativa em caso de falha e controle de concorrência entre os Workers.

> O processamento em si (enviar e-mail, gerar relatório...) é simulado, como permite o enunciado. O foco está na estrutura que sustenta essas operações.

## Como funciona

```
                 1. POST /tarefas
   Cliente ───────────────────────────► API (TK.Api)
                                          │  2. salva (Pendente)
                                          ▼
                                       MongoDB ◄──────────────┐
                                          │                   │
                         3. publica o Id  │                   │ 5. reserva, atualiza
                                          ▼                   │    o status e as tentativas
                                      RabbitMQ                │
                                    (fila-tarefas)            │
                                          │                   │
                         4. entrega o Id  ▼                   │
                                   Worker(s) (TK.Worker) ─────┘
```

1. O cliente envia uma tarefa para a API.
2. A API salva a tarefa no MongoDB com o status `Pendente`.
3. A API publica o **Id** da tarefa na fila do RabbitMQ.
4. Um Worker recebe o Id.
5. O Worker reserva a tarefa no banco, processa e atualiza o status até `Concluido` (ou `Erro`).

O cliente consulta o status a qualquer momento com `GET /tarefas/{id}`.

### Status das tarefas

| Status | Significado |
|---|---|
| `Pendente` | Aguardando um Worker |
| `EmProcessamento` | Reservada por um Worker |
| `Concluido` | Processada com sucesso |
| `Erro` | Falhou em todas as tentativas permitidas |

## Tecnologias

- C# e .NET 8 (ASP.NET Core e Worker Service)
- MongoDB 7 (banco NoSQL)
- RabbitMQ 3 (fila de mensagens)
- Docker e Docker Compose
- xUnit (testes) e GitHub Actions (integração contínua)

## Estrutura do projeto

```
src/
├── TK.Api/       API REST (controllers e DTOs)
├── TK.Worker/    Worker que consome a fila e ProcessadorTarefa (regras de processamento)
└── TK.Core/      Código compartilhado entre os dois
    ├── Entities/       Entidade Tarefa
    ├── Enums/          StatusTarefa
    ├── Interfaces/     Contratos (repositório e publicador)
    ├── Messaging/      Publicador do RabbitMQ
    ├── Repositories/   Acesso ao MongoDB
    └── Settings/       Classes de configuração
tests/
└── TK.Tests/     Testes unitários (xUnit)
```

`TK` é a abreviação de *Task*. A API e o Worker dependem do `TK.Core`, mas não dependem um do outro.

## Como executar

### Pré-requisitos

- [Docker](https://www.docker.com/) com Docker Compose

### Com Docker (recomendado)

```bash
git clone https://github.com/LeeoLoss/task-processor-dotnet.git
cd task-processor-dotnet
docker compose up --build -d
```

Isso sobe o MongoDB, o RabbitMQ, a API e um Worker. Confira com:

```bash
docker compose ps
```

| Serviço | Endereço |
|---|---|
| API (Swagger) | http://localhost:5099/swagger |
| Painel do RabbitMQ | http://localhost:15672 (usuário `guest`, senha `guest`) |
| MongoDB | http://localhost:27017 |

Para rodar **vários Workers** ao mesmo tempo:

```bash
docker compose up -d --scale worker=3
```

Para acompanhar os logs dos Workers e para encerrar:

```bash
docker compose logs -f worker
docker compose down        # mantém os dados
docker compose down -v     # apaga também os dados
```

### Localmente, sem Docker para a aplicação

Requer o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Suba só a infraestrutura:

```bash
docker compose up -d mongodb rabbitmq
```

Em dois terminais separados:

```bash
dotnet run --project src/TK.Api
dotnet run --project src/TK.Worker
```

A API fica em http://localhost:5099/swagger.

## Usando a API

### Criar uma tarefa

```bash
curl -X POST http://localhost:5099/tarefas \
  -H "Content-Type: application/json" \
  -d '{"tipo": "EnviarEmail", "dados": {"para": "teste@exemplo.com"}}'
```

Resposta `202 Accepted`:

```json
{
  "id": "c4492841-138e-49aa-b05a-1bc2d2c451a7",
  "tipo": "EnviarEmail",
  "dados": { "para": "teste@exemplo.com" },
  "status": "Pendente",
  "tentativas": 0,
  "mensagemErro": null,
  "criadaEm": "2026-11-15T00:15:30.564Z"
}
```

- `tipo`: obrigatório, até 100 caracteres (por exemplo `EnviarEmail` ou `GerarRelatorio`).
- `dados`: opcional, qualquer JSON.

### Consultar uma tarefa

```bash
curl http://localhost:5099/tarefas/{id}
```

Retorna `200` com a tarefa, ou `404` se o Id não existir.

## Testando as re-tentativas e a concorrência

O trabalho do Worker é simulado (5 segundos por tarefa). O campo `falharAte` dos `dados` força falhas para testar a re-tentativa:

| `dados` | Resultado esperado |
|---|---|
| `{}` | Conclui na 1ª tentativa |
| `{"falharAte": 3}` | Falha duas vezes e conclui na 3ª tentativa |
| `{"falharAte": 99}` | Falha em todas e termina em `Erro` após 3 tentativas |

Para testar a **concorrência**, rode dois ou mais Workers (`--scale worker=2`), crie várias tarefas seguidas e acompanhe `docker compose logs -f worker`: cada tarefa é processada uma única vez, dividida entre os Workers.

## Testes

```bash
dotnet test
```

Os testes são unitários e rodam sem MongoDB nem RabbitMQ, usando dublês em memória (`tests/TK.Tests/Fakes`). Cobrem:

- **`ProcessadorTarefa`:** conclusão, re-tentativa, status `Erro` ao esgotar as tentativas, tarefas já processadas, retomada de tarefa presa após reentrega e disputa entre vários Workers pela mesma tarefa.
- **`TarefasController`:** criação (que salva como `Pendente` e publica na fila) e consulta (`200` e `404`).

O pipeline do GitHub Actions (`.github/workflows/ci.yml`) compila o projeto e roda os testes a cada push e Pull Request para `dev` e `main`.

## Configuração

As configurações ficam nos `appsettings.json` de cada projeto e podem ser sobrescritas por variáveis de ambiente, usando `__` para separar os níveis (é o que o `docker-compose.yml` faz para apontar para os serviços `mongodb` e `rabbitmq`).

| Seção | Chave | Padrão |
|---|---|---|
| `Mongo` | `StringConexao`, `NomeBanco`, `NomeColecaoTarefas` | `mongodb://localhost:27017`, `task-processor`, `tarefas` |
| `RabbitMq` | `Host`, `Porta`, `Usuario`, `Senha`, `NomeFilaTarefas` | `localhost`, `5672`, `guest`, `guest`, `fila-tarefas` |
| `Processamento` | `MaximoTentativas`, `SegundosEntreTentativas`, `SegundosProcessamento` | `3`, `5`, `5` |

Exemplo: `RabbitMq__Host=rabbitmq` equivale a `"RabbitMq": { "Host": "rabbitmq" }`.

## Decisões de projeto

- **Só o Id vai na mensagem.** O estado completo da tarefa fica no MongoDB, que é a fonte da verdade. A mensagem é pequena e não existem dois lugares guardando os mesmos dados.
- **Reserva atômica.** Antes de processar, o Worker reserva a tarefa com uma única operação do MongoDB (`FindOneAndUpdate`) que só funciona se ela ainda estiver `Pendente`, e já conta a tentativa. Assim, dois Workers nunca processam a mesma tarefa ao mesmo tempo.
- **Confirmação manual (ack).** A mensagem só sai da fila depois que o Worker termina. Se ele cair no meio, o RabbitMQ devolve a mensagem para a fila, e outro Worker assume a tarefa (entrega *pelo menos uma vez*).
- **Um Worker por vez, por mensagem.** Cada Worker usa `prefetch = 1`: só recebe a próxima mensagem depois de concluir a atual, o que distribui bem o trabalho entre vários Workers.
- **Re-tentativa com limite.** Ao falhar, a tarefa volta a `Pendente`, espera alguns segundos e é recolocada na fila, até o limite de `MaximoTentativas`. Depois disso, fica com `Erro` e a mensagem de erro é guardada.
- **Fila e mensagens duráveis.** Elas sobrevivem a uma reinicialização do RabbitMQ.
- **Regras separadas da infraestrutura.** O `ProcessadorTarefa` decide o que fazer com cada tarefa e não conhece o RabbitMQ, o que permite testá-lo com dublês em memória. O `Worker` só cuida de receber e confirmar as mensagens.
- **Interfaces no `TK.Core`.** A API e o Worker dependem de contratos (`ITarefaRepositorio`, `IPublicadorTarefa`), e não do MongoDB ou do RabbitMQ diretamente.
- **Cliente oficial do RabbitMQ, sem framework.** Usei o `RabbitMQ.Client` para deixar explícito o controle de confirmação (ack), a re-tentativa e a concorrência. Em um projeto de produção, o MassTransit seria uma alternativa, com re-tentativa, *outbox* e outros recursos prontos (a versão 9 tem licença comercial, vale conferir antes de adotar).

## Limitações conhecidas

- A API salva a tarefa e depois publica na fila. Se o RabbitMQ estiver fora do ar nesse momento, a tarefa fica `Pendente` no banco sem mensagem. Uma melhoria seria reenviar periodicamente as tarefas pendentes.
- A espera entre tentativas é feita dentro do Worker, que fica ocupado durante esse tempo. Uma alternativa seria usar filas de espera com TTL no RabbitMQ.
- As credenciais (`guest`/`guest`) são só para desenvolvimento. Em produção, devem vir de variáveis de ambiente ou de um gerenciador de segredos.
- A API não tem autenticação.
