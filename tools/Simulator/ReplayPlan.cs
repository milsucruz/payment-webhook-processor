namespace Simulator;

// Details: docs/code/ReplayMode.md
public static class ReplayPlan
{
    public static string[] Build(IReadOnlyList<string> payloads, Random random)
    {
        string[] deliveries = [.. payloads, .. payloads];
        random.Shuffle(deliveries);

        return deliveries;
    }
}
