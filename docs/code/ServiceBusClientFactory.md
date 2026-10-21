# ServiceBusClientFactory

`src/Payments.Infrastructure/Messaging/ServiceBusClientFactory.cs`

Builds the `ServiceBusClient` that the API and the worker register as a singleton. It exists so the choice between a connection string and a managed identity lives in one place (task 4.6) instead of being repeated in both `Program.cs` files.

## The two modes

| Configuration | Client | Used in |
| --- | --- | --- |
| `ServiceBus:ConnectionString` | `new ServiceBusClient(connectionString, options)` | Local: the emulator, in `appsettings.Development.json` and in compose |
| `ServiceBus:FullyQualifiedNamespace` | `new ServiceBusClient(namespace, new DefaultAzureCredential(), options)` | Cloud: `sb-....servicebus.windows.net`, set by the Bicep |

Only the configuration changes between environments, as the task asks. The code path that sends and receives is the same.

## Exactly one endpoint

`ServiceBusOptions.HasSingleEndpoint` is true only when one of the two keys is set. Both `Program.cs` files use it in `ValidateOnStart`, so a missing or ambiguous setting stops the process at startup. With both set, one of them would be silently ignored, and a developer who copies a local connection string into the cloud settings would not notice.

## Which identity `DefaultAzureCredential` picks

The Container Apps use user-assigned identities. With one of those, `DefaultAzureCredential` needs to be told which one, through the `AZURE_CLIENT_ID` environment variable (the identity's client ID, not a secret). The Bicep sets it per app. Without it the credential asks the platform for the default identity, and a container with a user-assigned identity only fails at the first send or receive with an authorization error, not at startup. The same variable makes the passwordless SQL connection pick the right identity (see `docs/azure/tasks/4.6-identidade-gerenciada-nas-conexoes.md`).

Locally nothing sets `AZURE_CLIENT_ID`, and `DefaultAzureCredential` falls back to the developer's `az login`, which is how the cloud namespace can also be reached from the machine.

## Why not another credential

`ManagedIdentityCredential(clientId)` would be more direct in the cloud, but it fails on the developer's machine. `DefaultAzureCredential` is the one credential that works in both places without a code branch. The cost is that it tries a chain of sources, which is slower on the first token; the first token is acquired once per process, not per message.

## Tests

`tests/Payments.Infrastructure.Tests/Messaging/ServiceBusClientFactoryTests.cs` checks `HasSingleEndpoint` and that each mode yields a client for the expected namespace. Creating a `ServiceBusClient` does not open a connection and `DefaultAzureCredential` does not ask for a token until the first operation, so the tests need neither the emulator nor Azure. The real identity is checked in the cloud (task 4.6 document).
