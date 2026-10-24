# Processador de Webhooks de Pagamento (Azure)

Recebe webhooks do Stripe em modo de teste, aplica cada evento uma única vez ao estado dos pagamentos e mostra o estado ao vivo num painel. É um projeto de portfólio: o objetivo é que o autor consiga explicar cada decisão, então código simples e legível vale mais que código esperto.

## Onde está o contexto

- `docs/architecture.md`: desenho completo do sistema. Leia só as seções que a tarefa pede.
- `docs/tasks.md`: tarefas por marco, com horas, critério de pronto e a coluna "Done" (✅ feita, ⬜ pendente). O fim do arquivo tem o registro de sessões ("Session log"), que diz onde o projeto está.
- `docs/pending.md`: pendências encontradas numa tarefa e fora do escopo dela, com a tarefa em que devem ser resolvidas.
- `docs/code/`: um documento por classe com a explicação detalhada que não cabe nos comentários do código. Leia o da classe antes de alterá-la.
- `docs/azure/`: um documento por serviço do Azure e um por tarefa do M4, em português (ver "Modo Professor"). Leia o do serviço antes de mexer nele.
- `docs/adr/`: decisões de arquitetura, em inglês. Os números 0001 a 0010 ficam reservados para as dez decisões da seção 12, escritas na tarefa 6.2; até lá, a fonte delas é essa seção. Decisões posteriores numeram a partir de 0011 (a primeira é a 0011, da tarefa 4.10).

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

## Modo Professor (M4)

Na fase 4 o autor ainda não conhece os serviços do Azure. A explicação vira documento versionado em `docs/azure/`, em português (exceção à regra de inglês de `docs/`: o objetivo é o autor entender e defender; `docs/azure/README.md` é o índice). Dois tipos de documento:

- **`docs/azure/<servico>.md`**: um por serviço (ex.: `service-bus.md`, `azure-sql.md`, `managed-identity-rbac.md`). Escrito na primeira tarefa que usa o serviço e só atualizado depois. Seções, nesta ordem:
  1. **O que é**, em linguagem simples e com uma analogia.
  2. **Equivalente local** que o projeto já usa (Service Bus ↔ emulador no compose; Key Vault ↔ `user-secrets`).
  3. **Por que ele e não a alternativa**, citando a decisão da seção 12 ou a restrição de custo da seção 10.
  4. **Camada/SKU escolhida**, o que ela não tem, a quota gratuita e o que acontece quando acaba.
  5. **Como entra no nosso sistema**, com um diagrama Mermaid curto e um exemplo concreto do fluxo (ex.: "o worker lê `payment-events` assim...").
  6. **Glossário** dos termos do serviço.
  7. **O que pode dar errado e quanto custa errar**: o que gera cobrança ou é difícil de desfazer.
  8. **Como conferir** no portal e na CLI (`az ...`).
- **`docs/azure/tasks/<N.N>-<nome>.md`**: um por tarefa do M4, escrito no fim dela a partir do que está no repositório, não do planejado: recursos criados, o Bicep ou script comentado trecho a trecho, comandos rodados com a saída, o que deu errado e como foi resolvido.

Regras:

- **Antes da aprovação**, o plano da tarefa traz: um bloco "Gera cobrança ou é difícil de desfazer" no topo (ou "nada" quando não há); um resumo de 3 a 5 linhas do que a tarefa cria e por quê; e o link para o doc do serviço. Se o doc do serviço ainda não existe, ele é escrito e mostrado antes de o plano ser aprovado.
- Um conceito reutilizado em outra tarefa (ex.: managed identity em 4.2, 4.3 e 4.6) é explicado por completo no doc do serviço na primeira vez; depois, o plano só linka e relembra.
- Cada conceito novo no chat vem com um exemplo concreto do nosso fluxo. O plano termina com 2 ou 3 perguntas de checagem para o autor responder antes de criar o recurso.
- Ao concluir a tarefa, o resumo diz o que foi criado e por que cada escolha foi feita, como já exige "Como trabalhar", e o doc da tarefa entra no mesmo PR da tarefa.
- A nota de estudo do M4 (`notes/m4-nuvem-explicado.md`) linka para esses docs em vez de duplicá-los.
- A regra vale só no M4 e não substitui as de custo: nenhum recurso é criado sem a aprovação do plano.

## Regras do desenho que não mudam sem ADR

- A API não grava no banco no caminho do webhook: valida a assinatura, publica na fila e responde.
- A assinatura do Stripe é validada sobre o corpo bruto da requisição, antes de qualquer desserialização.
- A chave de idempotência é o ID do evento (`evt_...`). Inbox, estado e histórico são gravados na mesma transação.
- O worker completa a mensagem só depois do commit. A conclusão automática fica desligada.
- O worker não reordena eventos. Um evento só é aplicado se levar o pagamento a um nível maior, conforme a tabela da seção 6.
- Erro transitório e conflito de concorrência são repetidos dentro da entrega; se persistirem, a mensagem volta para a fila. Erro permanente (evento ilegível, tipo não suportado ou conflito impossível) vai direto para a dead-letter com motivo. Toda mensagem que volta para a fila, inclusive por erro não classificado, vai para a dead-letter como `MaxDeliveryCountExceeded` depois de 5 entregas.
- Service Bus na camada Basic: sem sessões, sem detecção de duplicatas, sem tópicos.
- O projeto Domain não referencia nenhum outro projeto nem pacote do Azure.

## Convenções de código

- .NET 10 e C# com nullable habilitado e avisos tratados como erro.
- Controllers padrão, sem Minimal API. Sem MediatR e sem biblioteca paga ou de licença comercial.
- Camadas (seção 11 da arquitetura):
  - Controller e worker são adaptadores: traduzem HTTP ou mensagem em chamada de caso de uso, e o resultado em código HTTP ou em conclusão da mensagem. Não têm `try`/`catch`, não leem o conteúdo do evento e não referenciam `Payments.Infrastructure`, que só aparece em `Program.cs` para registrar as implementações.
  - Caso de uso é uma classe concreta em Application, sem interface, com um único método público `ExecuteAsync`. Desfechos esperados, inclusive falha de infraestrutura que ele sabe tratar, voltam como um enum de resultado, não como exceção. Quando o chamador precisa do motivo (ex.: o dead-letter), o retorno é um record com o enum e o motivo. O caso de uso registra o log desses desfechos.
  - Toda dependência externa de um caso de uso é um port (interface) declarado em Application e implementado em Infrastructure.
- Entidades (seção 5 da arquitetura):
  - Propriedades `public get; private set;` e um construtor que recebe todos os campos obrigatórios; o EF Core usa esse construtor ao ler do banco.
  - O estado só muda por métodos com nome do domínio (ex.: `Apply`), nunca por setter público ou `SetX`.
  - Registros que só são inseridos (inbox, histórico) usam `required` e `init`.
  - Classes `sealed`, sem construtor vazio e sem `virtual`, enquanto não houver lazy loading.
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
- Fixtures de eventos reais do Stripe ficam em `tests/fixtures` e não são editadas à mão. A única alteração é trocar o valor depois de `_secret_` no `client_secret` por `redacted`, para não versionar nem o segredo de teste. O `.gitattributes` impede a conversão de fim de linha nelas.

## Comandos

```bash
cp .env.example .env            # uma vez; preencha MSSQL_SA_PASSWORD, STRIPE_WEBHOOK_SECRET e ADMIN_API_KEY
stripe listen --print-secret    # valor de STRIPE_WEBHOOK_SECRET; depois de mudar o .env, docker compose up -d api
docker compose up -d            # SQL Server, emulador do Service Bus, migrator (aplica as migrations e sai), API e worker
curl http://localhost:5300/health   # emulador pronto quando responde 200; antes disso o webhook responde 500
start http://localhost:8080/        # painel (código em web/, servido pela API; depois de mudar web/, docker compose up -d --build api)
dotnet build
dotnet user-secrets set ConnectionStrings:Payments "Server=localhost,1433;Database=Payments;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True" --project src/Payments.Worker   # só para dotnet run do worker fora do compose; pare o worker do compose antes
dotnet test --filter "FullyQualifiedName!~Payments.IntegrationTests"   # testes de unidade, sem Docker
dotnet test tests/Payments.IntegrationTests                             # integração; precisa do Docker rodando
dotnet ef migrations add <Nome> -p src/Payments.Infrastructure -s src/Payments.Migrator -o Persistence/Migrations   # nova migration; depois, docker compose up -d --build migrator
stripe listen --events payment_intent.created,payment_intent.processing,payment_intent.requires_action,payment_intent.succeeded,payment_intent.payment_failed,payment_intent.canceled,charge.refunded --forward-to localhost:8080/webhooks/stripe
stripe trigger payment_intent.succeeded --remove payment_intent:payment_method_types --add "payment_intent:automatic_payment_methods[enabled]=true" --add "payment_intent:automatic_payment_methods[allow_redirects]=never"
stripe trigger payment_intent.succeeded --remove payment_intent:payment_method_types --add "payment_intent:automatic_payment_methods[enabled]=true" --add "payment_intent:automatic_payment_methods[allow_redirects]=never" --add "payment_intent:metadata[demo_fault]=poison"   # ou transient, slow; o worker do compose liga FaultInjection:Enabled
dotnet user-secrets set ConnectionStrings:Payments "<a mesma do worker>" --project tools/Simulator   # replay; ou ConnectionStrings__Payments no ambiente
dotnet user-secrets set Stripe:WebhookSecret <STRIPE_WEBHOOK_SECRET do .env> --project tools/Simulator   # replay; ou Stripe__WebhookSecret
dotnet user-secrets set Stripe:SecretKey <sk_test_...> --project tools/Simulator   # pay e refund; só aceita chave de teste
dotnet run --project tools/Simulator -- pay                          # um pagamento por cartão de teste: aprova, recusa, pede autenticação
dotnet run --project tools/Simulator -- refund <pi_...> [centavos]   # sem valor, reembolso total
dotnet run --project tools/Simulator -- replay [seed]                # reenvia cada evento do inbox duas vezes, embaralhado; precisa do compose de pé
dotnet run --project tools/Simulator -- flood [quantidade] [fault]   # N pagamentos novos (padrão 100), 8 por segundo, com demo_fault=fault (slow, o padrão, transient ou poison); na nuvem, Simulator__WebhookUrl e Stripe__WebhookSecret do Key Vault
$env:SQL_ADMIN_LOGIN = "<seu UPN>"; $env:SQL_ADMIN_OBJECT_ID = "<seu object id>"; $env:API_IMAGE_TAG = "<tag>"; $env:WORKER_IMAGE_TAG = "<tag>"; $env:MIGRATOR_IMAGE_TAG = "<tag>"; $env:ALERT_EMAIL = "<e-mail dos alertas>"; az deployment group create -g rg-payment-webhooks -p infra/main.bicepparam   # PowerShell; deploy manual do Bicep (o normal é o deploy.yml num push em main); as duas primeiras definem o admin Entra do SQL, as três tags não têm padrão, ALERT_EMAIL vai para o action group (o pipeline o lê do action group existente) (no bash, export)
pwsh infra/pipeline/setup-oidc.ps1   # identidade do pipeline: app registration, credencial federada de main, Contributor e RBAC Admin condicionado no grupo, variáveis AZURE_* no GitHub; idempotente, rode de novo depois de recriar o grupo ou de mudar a lista de papéis permitidos (antes do deploy que usa o papel novo)
pwsh infra/stripe/register-webhook.ps1   # registra o endpoint da nuvem no sandbox do Stripe (7 eventos, API 2026-09-30.endive), grava o whsec_ no Key Vault e reinicia a API; para se o endpoint já existir (o Stripe só mostra o segredo na criação): apague-o com stripe webhook_endpoints delete <we_...> e rode de novo
stripe webhook_endpoints update <we_...> -d disabled=true   # para de mandar eventos à nuvem (o stripe trigger local também vai para lá); disabled=false religa
az role assignment create --role "Key Vault Secrets Officer" --assignee <seu object id> --scope $(az keyvault list -g rg-payment-webhooks --query "[0].id" -o tsv)   # libera o az keyvault secret set para você; feito à mão porque a condição do pipeline não permite esse papel; refaça depois de recriar o grupo
az role assignment create --role "Azure Service Bus Data Receiver" --assignee <seu object id> --scope "$(az servicebus namespace list -g rg-payment-webhooks --query "[0].id" -o tsv)/queues/payment-events"   # libera o purge abaixo para você; à mão, como o de cima
pwsh infra/servicebus/purge-dead-letters.ps1   # apaga tudo da dead-letter de payment-events na nuvem (receive-and-delete via REST com o seu token); o alerta de dead-letter só avisa de novo depois disso
az rest --method post --url "https://api.loganalytics.io/v1/workspaces/$(az resource show -g rg-payment-webhooks -n log-payment-webhooks --resource-type Microsoft.OperationalInsights/workspaces --query properties.customerId -o tsv)/query" --resource https://api.loganalytics.io --body "{\"query\": \"union AppRequests, AppDependencies | where OperationId == '<trace ID>' | order by TimeGenerated asc | project TimeGenerated, AppRoleName, Name, Id, ParentId\"}" --query "tables[0].rows" -o tsv   # spans de um trace no Application Insights (bash; o trace ID está nos logs, , e na timeline do painel); a ingestão leva alguns minutos
az sql server firewall-rule create -g rg-payment-webhooks -s <servidor> -n developer --start-ip-address <seu IP> --end-ip-address <seu IP>   # libera o seu IP no banco da nuvem
pwsh infra/sql/create-users.ps1 -ResourceGroup rg-payment-webhooks -ServerName <servidor> -SqlCmd <caminho do go-sqlcmd>   # cria os usuários das identidades no banco; precisa do go-sqlcmd (winget install sqlcmd) e de az login como admin Entra
```

Atualize esta seção quando um comando mudar. Todos funcionam; `pay` e `refund` precisam da chave secreta do sandbox em que o endpoint da nuvem está registrado (o `account_id` de `stripe config --list`; confira com `curl -s -u <chave>: https://api.stripe.com/v1/account`, que responde o `id` da conta da chave). A chave de outra conta cria pagamentos cujos webhooks não chegam ao sistema.

- O `stripe listen` exige a lista de eventos desde o CLI 1.53; a lista é a dos eventos assinados (seção 2 da arquitetura).
- O `trigger` remove `payment_method_types` porque a versão de API da conta (`2026-09-30.endive`) rejeita esse parâmetro, que a fixture padrão do CLI envia.

## Custo

- Do M0 ao M3 nada é criado no Azure. Não proponha criar recurso de nuvem antes da tarefa 4.1.
- No Azure, só as camadas da seção 10: Service Bus Basic, banco SQL da oferta gratuita, Container Apps em Consumption com mínimo de zero réplicas.

## Agent skills

### Issue tracker

Issues live in GitHub Issues for `milsucruz/payment-webhook-processor`, via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` and `docs/adr/` at the repo root. `CONTEXT.md` doesn't exist yet: the glossary is "Terms used in this document" in section 1 of `docs/architecture.md`. `docs/adr/` starts at 0011; 0001 to 0010 are reserved for the ten decisions of section 12 (task 6.2). See `docs/agents/domain.md`.
