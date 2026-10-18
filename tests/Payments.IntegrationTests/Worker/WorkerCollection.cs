namespace Payments.IntegrationTests.Worker;

[CollectionDefinition(Name)]
public sealed class WorkerCollection : ICollectionFixture<WorkerFixture>
{
    public const string Name = "Worker";
}
