# Roteiro de testes: Swagger e painel do RabbitMQ

## Ferramentas usadas

- **Swagger:** `http://localhost:5099/swagger`  
  Em cada teste: `POST /tarefas` → *Try it out* → cole o JSON no campo *Request body* → *Execute*. Para consultar, use `GET /tarefas/{id}` colando o `id` devolvido pelo POST.
- **Painel do RabbitMQ:** `http://localhost:15672` (`guest` / `guest`) → aba **Queues and Streams** → `fila-tarefas`.  
  Deixe essa tela aberta numa segunda aba do navegador e atualize durante os testes.
- **Terminal (VS Code):** só para subir o ambiente, ver os logs do Worker e derrubar/religar containers.

---

## 0. Preparação

Suba os containers e verifique o status:

```bash
docker compose up --build -d
docker compose ps
```

Em um segundo terminal, deixe os logs do Worker rodando:

```bash
docker compose logs -f worker
```

| # | Verificação | Esperado |
| :--- | :--- | :--- |
| **0.1** | `docker compose ps` | `tk-mongodb` e `tk-rabbitmq` healthy; `tk-api` e o `worker` em execução |
| **0.2** | Swagger abre | `POST /tarefas` e `GET /tarefas/{id}` listados |
| **0.3** | RabbitMQ → Queues → `fila-tarefas` | Fila existe, Consumers: `1`, Ready `0`, Unacked `0` |
| **0.4** | Log do Worker | `Worker aguardando tarefas na fila 'fila-tarefas'...` |

---

## 1. Criação e consulta

Cole cada corpo no `POST /tarefas`:

### 1.1 E-mail com dados
```json
{
  "tipo": "EnviarEmail",
  "dados": {
    "para": "teste@exemplo.com",
    "assunto": "Bem-vindo",
    "corpo": "Olá!"
  }
}
```

### 1.2 Relatório com dados
```json
{
  "tipo": "GerarRelatorio",
  "dados": {
    "relatorio": "vendas-mensal",
    "mes": 11,
    "ano": 2026
  }
}
```

### 1.3 Sem o campo dados
```json
{
  "tipo": "EnviarEmail"
}
```

### 1.4 Tipo com espaços nas pontas
```json
{
  "tipo": "  EnviarEmail  ",
  "dados": {}
}
```

### 1.5 dados nulo
```json
{
  "tipo": "EnviarEmail",
  "dados": null
}
```

### 1.6 dados como lista (qualquer JSON é aceito)
```json
{
  "tipo": "GerarRelatorio",
  "dados": [1, 2, 3]
}
```

| # | Esperado no POST | Esperado no GET (depois de ~6 s) |
| :--- | :--- | :--- |
| **1.1** | `202`, status: `"Pendente"`, tentativas: `0`, id GUID, criadaEm de hoje | `Concluido`, tentativas: `1`, dados iguais aos enviados |
| **1.2** | `202`, mesmo padrão | `Concluido`, tentativas: `1` |
| **1.3** | `202`, `"dados": {}` | `Concluido` |
| **1.4** | `202`, `"tipo": "EnviarEmail"` sem espaços | `Concluido` |
| **1.5** | `202`, `"dados": {}` | `Concluido` |
| **1.6** | `202`, `"dados": [1,2,3]` | `Concluido` |

> **Observação no RabbitMQ:** Logo depois de um POST, atualize a página da fila: em cerca de 5 s a tarefa aparece em **Unacked 1** (sendo processada) e depois tudo volta a **0** (mensagem confirmada).

---

## 2. Validações e erros

### 2.1 Sem tipo → esperado `400`
```json
{
  "dados": {}
}
```

### 2.2 tipo vazio → `400`
```json
{
  "tipo": "",
  "dados": {}
}
```

### 2.3 tipo só com espaços → `400`
```json
{
  "tipo": "     ",
  "dados": {}
}
```

### 2.4 tipo com número → `400`
```json
{
  "tipo": 123
}
```

### 2.5 JSON inválido → `400`
```json
{ "tipo": "EnviarEmail",
```

### 2.6 Corpo vazio → `400`
Apague todo o conteúdo do campo *Request body* e execute.

### 2.7 tipo com exatamente 100 caracteres → `202` (limite aceito)
```json
{
  "tipo": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
}
```

### 2.8 tipo com 101 caracteres → `400`
Mensagem esperada: `O campo 'Tipo' deve ter no máximo 100 caracteres.`
```json
{
  "tipo": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
}
```

### Consultas com erro (`GET /tarefas/{id}`)

| # | Valor de `id` | Esperado |
| :--- | :--- | :--- |
| **2.9** | `00000000-0000-0000-0000-000000000001` | `404`, `Tarefa com ID '...' não encontrada.` |
| **2.10** | `abc` | `404` (a rota exige GUID). Se o Swagger bloquear o envio, abra `http://localhost:5099/tarefas/abc` no navegador |

> **No RabbitMQ:** nenhum dos casos com `400` deve gerar mensagem. A fila continua em `0` e o contador *Total* não aumenta.

---

## 3. Re-tentativas

Cada tentativa leva **5 s**, com **5 s** de espera entre elas, então o resultado final sai em **~25 s**.

### 3.1 Conclui na 1ª tentativa
```json
{
  "tipo": "EnviarEmail",
  "dados": {}
}
```

### 3.2 Falha duas vezes e conclui na 3ª
```json
{
  "tipo": "EnviarEmail",
  "dados": {
    "falharAte": 3
  }
}
```

### 3.3 Falha em todas e termina em Erro
```json
{
  "tipo": "EnviarEmail",
  "dados": {
    "falharAte": 99
  }
}
```

### 3.4 Falha só uma vez
```json
{
  "tipo": "GerarRelatorio",
  "dados": {
    "falharAte": 2
  }
}
```

Para os testes **3.2** e **3.3**, faça `GET` a cada ~5 s e compare com a tabela:

| # | Momento | `status` | `tentativas` | `mensagemErro` |
| :--- | :--- | :--- | :--- | :--- |
| **3.1** | 0 a 5 s | `EmProcessamento` | `1` | `null` |
| **3.1** | depois de 5 s | `Concluido` | `1` | `null` |
| **3.2** | 0 a 5 s | `EmProcessamento` | `1` | `null` |
| **3.2** | ~5 a 10 s | `Pendente` | `1` | `Falha simulada na tentativa 1.` |
| **3.2** | ~10 a 15 s | `EmProcessamento` | `2` | `Falha simulada na tentativa 1.` |
| **3.2** | ~15 a 20 s | `Pendente` | `2` | `Falha simulada na tentativa 2.` |
| **3.2** | depois de ~25 s | `Concluido` | `3` | `Falha simulada na tentativa 2.` *(histórico mantido)* |
| **3.3** | depois de ~25 s | `Erro` | `3` | `Falha simulada na tentativa 3.` |
| **3.4** | depois de ~15 s | `Concluido` | `2` | `Falha simulada na tentativa 1.` |

*Os tempos são aproximados, porque dependem de quando você consulta.*

**No RabbitMQ, durante o 3.2:**
- A mensagem fica em `Unacked 1` durante todo o ciclo, inclusive nas esperas de 5 s;
- Em cada espera, o *Total* pode mostrar `2` por instantes: a original ainda não confirmada e a nova, republicada para a próxima tentativa;
- Ao final tudo volta a `0`.

**No log do Worker:**
- **3.2:** dois `Falha na tentativa ... Nova tentativa em 5s` e depois `Tarefa ... concluída.`
- **3.3:** dois avisos de nova tentativa e `Tarefa ... falhou após 3 tentativa(s) e foi marcada com Erro.`

---

## 4. Concorrência com vários Workers

Escale o Worker para 3 instâncias:

```bash
docker compose up -d --scale worker=3
```

| # | Verificação | Esperado |
| :--- | :--- | :--- |
| **4.1** | RabbitMQ → `fila-tarefas` | Consumers: `3` |

Agora, no Swagger, execute o POST abaixo **9 vezes seguidas**, o mais rápido possível (cole uma vez e clique em *Execute* repetidamente):

```json
{
  "tipo": "EnviarEmail",
  "dados": {
    "para": "teste@exemplo.com"
  }
}
```

Anote os 9 `id`s. Enquanto processa, atualize a tela da fila:

| # | Verificação | Esperado |
| :--- | :--- | :--- |
| **4.2** | Painel do RabbitMQ no início | `Unacked 3` (um por Worker) e `Ready 6` |
| **4.3** | Painel depois de ~15 s | `Ready` e `Unacked` em `0` |
| **4.4** | Log do Worker | Prefixos `worker-1`, `-2` e `-3` todos aparecendo |
| **4.5** | Duplicidade | Cada `id` aparece em um único `Processando tarefa ...` |
| **4.6** | GET dos 9 `id`s | Todos `Concluido` com tentativas: `1` |

Para voltar a um Worker:

```bash
docker compose up -d --scale worker=1
```

---

## 5. Resiliência

Com 1 Worker. A fila é acompanhada pelo painel do RabbitMQ.

### 5.1 Worker cai no meio do processamento
1. No Swagger, crie `{ "tipo": "EnviarEmail", "dados": {} }`.
2. Em até 3 s, no terminal: `docker compose kill worker`
3. Confira no Swagger e no painel.
4. Suba de novo: `docker compose up -d worker`

| Momento | Swagger (GET) | RabbitMQ |
| :--- | :--- | :--- |
| Depois do `kill` | `EmProcessamento`, tentativas: `1` | Consumers `0`; a mensagem volta de Unacked para `Ready 1` |
| Depois do `up` | ~6 s depois: `Concluido`, tentativas: `2` | Consumers `1`; tudo volta a `0` |

### 5.2 Desligamento normal do Worker
1. Crie `{ "tipo": "EnviarEmail", "dados": {} }`.
2. Em até 3 s: `docker compose stop worker`
3. Depois: `docker compose up -d worker`

**Esperado:** no log, `Worker encerrando.`; a mensagem volta para `Ready 1` no painel; depois do `up`, a tarefa termina `Concluido`.

### 5.3 Mensagens inválidas na fila
No painel: **Queues** → `fila-tarefas` → **Publish message**. Em *Properties*, deixe vazio, e preencha o *Payload*:

| # | Payload | Esperado no log do Worker | RabbitMQ |
| :--- | :--- | :--- | :--- |
| **5.3.1** | `texto-invalido` | `Mensagem inválida recebida: 'texto-invalido'. Descartada.` | Volta a `0` |
| **5.3.2** | `3f2b8c1e-9d4a-4e5f-8a6b-1c2d3e4f5a6b` | `... não pôde ser reservada ... Ignorando.` | Volta a `0` |

### 5.4 Persistência dos dados
```bash
docker compose down
docker compose up -d
```
**Esperado:** o GET de um `id` antigo continua devolvendo a tarefa, e a fila `fila-tarefas` continua existindo no painel.

### 5.5 RabbitMQ fora do ar (limitação conhecida)
```bash
docker compose stop rabbitmq
```
No Swagger, `POST /tarefas` com `{ "tipo": "EnviarEmail", "dados": {} }`. **Esperado:** `500`. Depois:

```bash
docker compose exec mongodb mongosh task-processor --eval "db.tarefas.find({Status:'Pendente'})"
docker compose start rabbitmq
```
**Esperado:** a tarefa aparece `Pendente` no banco, sem mensagem na fila, como descrito nas limitações do README. Se o Worker tiver caído, `docker compose up -d` o religa.

---

## 6. Testes automatizados e CI

| # | Ação | Esperado |
| :--- | :--- | :--- |
| **6.1** | `dotnet build` | 0 avisos, 0 erros |
| **6.2** | `dotnet test` (ou *Testing → Run All* no VS Code) | 13 aprovados |
| **6.3** | Aba **Actions** do repositório no GitHub | Último run da `main` verde em Ubuntu, Windows e macOS |

---

## 7. Encerramento

```bash
docker compose down -v
```

---

## Checklist final

- [ ] **0. Preparação** (4 casos)
- [ ] **1. Criação e consulta** (6 casos)
- [ ] **2. Validações e erros** (10 casos)
- [ ] **3. Re-tentativas** (4 casos)
- [ ] **4. Concorrência** (6 casos)
- [ ] **5. Resiliência** (5 cenários)
- [ ] **6. Testes automatizados e CI** (3 casos)
