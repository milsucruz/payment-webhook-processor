# Azure: serviços e tarefas do M4

Documentos em português (exceção à regra de inglês de `docs/`, ver "Modo Professor" no `CLAUDE.md`). Servem para o autor entender e defender cada recurso criado na nuvem.

## Serviços

| Documento | Serviço | Usado em |
| --- | --- | --- |
| [service-bus.md](service-bus.md) | Azure Service Bus (Basic) | 4.2, 4.6, 4.7 |
| [managed-identity-rbac.md](managed-identity-rbac.md) | Identidade gerenciada e RBAC | 4.2, 4.3, 4.6, 4.8 |
| [azure-sql.md](azure-sql.md) | Azure SQL Database (oferta gratuita) | 4.3, 4.6 |
| [key-vault.md](key-vault.md) | Azure Key Vault | 4.4, 4.5, 4.9 |
| [container-apps.md](container-apps.md) | Azure Container Apps (Consumption) | 4.5, 4.6, 4.7, 4.8 |
| [log-analytics-application-insights.md](log-analytics-application-insights.md) | Log Analytics e Application Insights | 4.4, 5.6, 5.7 |

O de OIDC do GitHub Actions é escrito na primeira tarefa que o usa (4.8).

## Tarefas

| Documento | Tarefa |
| --- | --- |
| [tasks/4.2-identidades-e-service-bus.md](tasks/4.2-identidades-e-service-bus.md) | 4.2 Identidades e Service Bus |
| [tasks/4.3-sql-gratuito.md](tasks/4.3-sql-gratuito.md) | 4.3 Banco SQL gratuito |
| [tasks/4.4-key-vault-e-monitoramento.md](tasks/4.4-key-vault-e-monitoramento.md) | 4.4 Key Vault e monitoramento |
| [tasks/4.5-container-apps.md](tasks/4.5-container-apps.md) | 4.5 Container Apps |
| [tasks/4.6-identidade-gerenciada-nas-conexoes.md](tasks/4.6-identidade-gerenciada-nas-conexoes.md) | 4.6 Identidade gerenciada nas conexões |

A tarefa 4.1 (conta, orçamento e grupo de recursos) foi feita à mão no portal e não tem Bicep para comentar; ficou registrada no "Session log" de `docs/tasks.md`.

## Como ler

1. Leia o documento do serviço (o que é, por que, como entra no sistema).
2. Leia o da tarefa (o que foi criado de fato, linha a linha, e o que deu errado).
3. Confira com os comandos `az` da última seção de cada documento.
