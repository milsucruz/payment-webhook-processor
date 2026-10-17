namespace Simulator.Tests;

public class ReplayPlanTests
{
    private static readonly string[] payloads = ["a", "b", "c", "d", "e", "f", "g", "h"];

    [Fact]
    public void Every_payload_is_delivered_exactly_twice()
    {
        string[] deliveries = ReplayPlan.Build(payloads, new Random(42));

        Assert.Equal(payloads.Length * 2, deliveries.Length);
        foreach (string payload in payloads)
        {
            Assert.Equal(2, deliveries.Count(delivery => delivery == payload));
        }
    }

    [Fact]
    public void Deliveries_are_not_in_the_original_order()
    {
        string[] inOrder = [.. payloads, .. payloads];

        string[] deliveries = ReplayPlan.Build(payloads, new Random(42));

        Assert.NotEqual(inOrder, deliveries);
    }

    [Fact]
    public void The_same_seed_gives_the_same_order()
    {
        string[] first = ReplayPlan.Build(payloads, new Random(7));
        string[] second = ReplayPlan.Build(payloads, new Random(7));

        Assert.Equal(first, second);
    }

    [Fact]
    public void No_payloads_give_no_deliveries()
    {
        string[] deliveries = ReplayPlan.Build([], new Random(42));

        Assert.Empty(deliveries);
    }
}
