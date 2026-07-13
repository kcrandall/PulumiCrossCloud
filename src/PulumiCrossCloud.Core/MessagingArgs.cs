namespace PulumiCrossCloud.Core;

/// <summary>
/// Configuration for creating a point-to-point queue.
/// </summary>
public record QueueArgs
{
    public int? MaxDeliveryAttempts { get; init; }
    public int? VisibilityTimeoutSeconds { get; init; }
    public string? DeadLetterTargetArn { get; init; }

    /// <summary>
    /// Optional topic to subscribe this queue to (Fan-out pattern).
    /// </summary>
    public ITopicEndpoint? SubscribeTo { get; init; }

    /// <summary>
    /// If true, ensures first-in-first-out delivery and exactly-once processing where supported.
    /// </summary>
    public bool Fifo { get; init; } = false;
}

/// <summary>
/// Configuration for creating a pub/sub topic.
/// </summary>
public record TopicArgs
{
    /// <summary>
    /// If true, ensures first-in-first-out delivery where supported (e.g. SNS FIFO).
    /// </summary>
    public bool Fifo { get; init; } = false;

    /// <summary>
    /// If true, enables content-based deduplication for FIFO topics.
    /// </summary>
    public bool ContentBasedDeduplication { get; init; } = false;
}

/// <summary>
/// Configuration for creating a replayable event stream.
/// </summary>
public record StreamArgs
{
    /// <summary>
    /// Data retention period in hours.
    /// </summary>
    public int? RetentionHours { get; init; }

    /// <summary>
    /// Abstracted throughput units (e.g. Shards in Kinesis, Partitions in Kafka/Hubs).
    /// </summary>
    public int? ThroughputUnits { get; init; }
}
