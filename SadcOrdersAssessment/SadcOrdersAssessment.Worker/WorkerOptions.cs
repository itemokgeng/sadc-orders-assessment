namespace SadcOrders.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>Simulated work per message, so back-pressure and prefetch are visible in a demo.</summary>
    public int SimulatedProcessingDelayMs { get; set; } = 500;

    /// <summary>Chance (0 to 1) that processing throws, to exercise the retry and dead-letter path.</summary>
    public double SimulatedFailureRate { get; set; }
}
