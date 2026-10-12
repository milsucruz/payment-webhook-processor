# Processador de Webhooks de Pagamento (Azure)

Recebe webhooks do Stripe em modo de teste, aplica cada evento uma única vez ao estado dos pagamentos e mostra o estado ao vivo num painel. É um projeto de portfólio: o objetivo é que o autor consiga explicar cada decisão, então código simples e legível vale mais que código esperto.

## Onde está o contexto

- `docs/architecture.md`: desenho completo do sistema. Leia só as seções que a tarefa pede.
- `docs/tasks.md`: tarefas por marco, com horas, critério de pronto e a coluna "Done" (✅ feita, ⬜ pendente). O fim do arquivo tem o registro de sessões ("Session log"), que diz onde o projeto está.
- `docs/pending.md`: pendências encontradas numa tarefa e fora do escopo dela, com a tarefa em que devem ser resolvidas.
- `docs/code/`: um documento por classe com a explicação detalhada que não cabe nos comentários do código. Leia o da classe antes de alterá-la.
- `docs/adr/`: decisões de arquitetura, em inglês. A pasta ainda não existe; os ADRs são escritos na tarefa 6.2. Até lá, a fonte das decisões é a seção 12 da arquitetura, que lista as dez.

| Assunto | Seção de `docs/architecture.md` |
| --- | --- |
| Requisitos e metas | 2 |
| Componentes e diagrama | 3 |
| Fluxo do evento e comportamento em cada falha | 4 |
| Tabelas e chaves | 5 |
| Idempotência, máquina de estados, retry e dead-letter | 6 |
| Hub SignalR e telas do painel | 7 |
| Identidades, papéis e segredos | 8 |
| Traces, métricas e health checks | 9 |
| Bicep, custo, pipeline e ambiente local | 10 |
| Projetos da solução e estratégia de testes | 11 |
| Decisões e alternativas descartadas | 12 |

## Como trabalhar

- Uma tarefa por sessão. Espere eu dizer o número da tarefa de `docs/tasks.md`.
- Antes de escrever código, leia as seções citadas e proponha um plano curto. Só implemente depois que eu aprovar.
- Não escreva código fora do escopo da tarefa atual, mesmo que pareça adiantar a próxima.
- A tarefa só termina quando o "pronto quando" dela (coluna "Done when") foi verificado rodando. Mostre o comando e a saída.
- Se a tarefa pedir algo que contradiz a arquitetura, pare e avise. Não resolva a contradição sozinho.
- Ao explicar o que fez, diga o porquê de cada escolha. Eu preciso conseguir defender o código em entrevista.
- No fim da sessão, proponha a linha do registro de sessões ("Session log") em `docs/tasks.md` e troque o ⬜ da tarefa por ✅ na coluna "Done", no mesmo commit da linha. Só marque ✅ com o "pronto quando" verificado.
- No plano, diga se a alteração vai por PR ou direto em `main`, com base nos critérios abaixo. Eu posso mudar a decisão ao aprovar o plano.
  - **PR**: qualquer mudança em código, testes, CI, Docker/compose ou Bicep; e mudanças em documentação que alterem uma regra ou decisão (este arquivo, `docs/architecture.md`, ADRs, critérios em `docs/tasks.md`).
  - **Direto em `main`**: documentação que não muda regra nem decisão, como a linha do registro de sessões e o ✅ da tarefa, erro de digitação ou ajuste de redação.
- Quando for PR: branch a partir de `main`, nomeada `type/descricao-curta` (ex.: `feat/stripe-webhook-endpoint`). Com o "pronto quando" verificado e a linha do registro de sessões aprovada, faça push e abra o PR com `gh pr create`, sem pedir confirmação. Título no formato do commit; a descrição diz a tarefa, o que mudou e o comando de verificação com a saída. O merge fica comigo.
- Algo estritamente necessário e fora do escopo da tarefa vai para `docs/pending.md`, não para o código.
- Ao terminar a última tarefa de um marco (M0, M1, ...), revise `docs/pending.md` comigo: cada item é resolvido, vira tarefa ou é descartado com motivo.
- Depois dessa revisão, escreva a nota de estudo do marco em `notes/mN-<nome-do-marco>-explicado.md`, no mesmo formato de `notes/m0-esqueleto-explicado.md`:
  - Em português (exceção à regra de inglês: é nota pessoal, não versionada; `notes/` está em `.git/info/exclude`).
  - Cabeçalho dizendo que é nota pessoal e citando as tarefas de `docs/tasks.md` e as seções da arquitetura usadas.
  - Tabela das tarefas do marco (entrega e "pronto quando") e um parágrafo sobre o porquê da ordem.
  - Uma seção por tarefa: o que existe (trechos curtos do código ou da configuração real), o porquê de cada escolha, as alternativas descartadas e como o "pronto quando" foi verificado.
  - No fim, uma tabela "decisão / por quê em uma frase / origem" e uma lista de perguntas de entrevista com resposta curta.
  - Descreva o que está no repositório, não o planejado. Se algo divergiu da arquitetura ou ficou em `docs/pending.md`, diga.

## Regras do desenho que não mudam sem ADR

- A API não grava no banco no caminho do webhook: valida a assinatura, publica na fila e responde.
- A assinatura do Stripe é validada sobre o corpo bruto da requisição, antes de qualquer desserialização.
- A chave de idempotência é o ID do evento (`evt_...`). Inbox, estado e histórico são gravados na mesma transação.
- O worker completa a mensagem só depois do commit. A conclusão automática fica desligada.
- O worker não reordena eventos. Um evento só é aplicado se levar o pagamento a um nível maior, conforme a tabela da seção 6.
- Erro transitório é repetido; erro permanente vai direto para a dead-letter com motivo.
- Service Bus na camada Basic: sem sessões, sem detecção de duplicatas, sem tópicos.
- O projeto Domain não referencia nenhum outro projeto nem pacote do Azure.

## Convenções de código

- .NET 10 e C# com nullable habilitado e avisos tratados como erro.
- Controllers padrão, sem Minimal API. Sem MediatR e sem biblioteca paga ou de licença comercial.
- Nunca `var`, em nenhum caso: toda variável declara o tipo explícito (`string payload = ...`). Com `new`, use o `new(...)` com tipo alvo (`StreamReader reader = new(...)`). O `.editorconfig` faz o build falhar com `var` (IDE0008).
- Valores monetários em centavos, como `long`. Nunca `decimal`, `double` ou `float`.
- Datas em UTC, com `DateTimeOffset` no código e `datetime2` no banco.
- Identificadores, comentários, mensagens de commit, README e ADRs em inglês. Os arquivos em `docs/` ficam em inglês.
- Nomenclatura:
  - Todo método de produção que retorna `Task` ou `ValueTask` termina em `Async` (ex.: `ReceiveAsync`), inclusive actions de controller. Métodos de teste não levam o sufixo: o nome deles descreve o comportamento. O MVC remove o `Async` do nome da action por padrão, então `nameof(ReceiveAsync)` em `CreatedAtAction` não encontra a rota; use a rota por nome ou desligue `SuppressAsyncSuffixInActionNames`.
  - Método async público recebe `CancellationToken cancellationToken` como último parâmetro. Exceção só com motivo escrito em comentário no código, como a action do webhook, que não pode ter parâmetros.
  - Campos privados em `camelCase`, sem `_` (use `this.campo = campo` no construtor quando o nome colidir com o parâmetro). Interfaces com prefixo `I`. Constantes em `PascalCase` (ex.: `MaxBodyBytes`).
- Comentários no código são curtos: uma linha com o porquê, só onde o código não é óbvio. A explicação detalhada (alternativas, armadilhas do framework, como foi descoberto) vai para o documento da classe em `docs/code/<Classe>.md`, criado na primeira vez que a classe precisar. Vale para qualquer classe: controller, Application, Infrastructure, Worker.
- Toda action de controller tem `/// <summary>` com uma frase sobre o que o endpoint faz. Sem `<param>`, `<returns>` ou detalhe de implementação.
- Commits no formato `type: description` (`feat`, `fix`, `test`, `docs`, `chore`, `infra`), um por tarefa quando possível.
- Nenhum segredo no código, na imagem ou no repositório. Local usa o emulador e `dotnet user-secrets`; na nuvem, identidade gerenciada e Key Vault.

## Testes

- xUnit. Testes de integração com Testcontainers (SQL Server e emulador do Service Bus).
- Toda regra de domínio nasce com teste. A tabela de transições tem um caso por combinação de estado e evento.
- Fixtures de eventos reais do Stripe ficam em `tests/fixtures` (criada na tarefa 1.5) e não são editadas à mão.

## Comandos

```bash
cp .env.example .env            # uma vez; preencha MSSQL_SA_PASSWORD e STRIPE_WEBHOOK_SECRET
docker compose up -d            # SQL Server, emulador do Service Bus, API e worker
dotnet build
dotnet test --filter "FullyQualifiedName!~Payments.IntegrationTests"   # testes de unidade, sem Docker
dotnet test tests/Payments.IntegrationTests                             # integração; precisa do Docker rodando
stripe listen --forward-to localhost:8080/webhooks/stripe
stripe trigger payment_intent.succeeded
```

Atualize esta seção quando um comando mudar. Os cinco primeiros já funcionam. Os dois do Stripe dependem do endpoint `POST /webhooks/stripe` (tarefa 1.1) e só são usados de ponta a ponta na tarefa 1.5.

## Custo

- Do M0 ao M3 nada é criado no Azure. Não proponha criar recurso de nuvem antes da tarefa 4.1.
- No Azure, só as camadas da seção 10: Service Bus Basic, banco SQL da oferta gratuita, Container Apps em Consumption com mínimo de zero réplicas.

## Agent skills

### Issue tracker

Issues live in GitHub Issues for `milsucruz/payment-webhook-processor`, via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` and `docs/adr/` at the repo root. Neither exists yet: `docs/adr/` arrives in task 6.2, and until then the glossary is "Terms used in this document" in section 1 of `docs/architecture.md`. See `docs/agents/domain.md`.
