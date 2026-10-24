namespace Payments.IntegrationTests.Reading;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PaymentsDatabaseCollection : ICollectionFixture<PaymentsDatabaseFixture>
{
    public const string Name = "PaymentsDatabase";
}
