namespace PulumiCrossCloud.Core;

/// <summary>
/// Specifies the type of messaging resource.
/// </summary>
public enum MessagingKind
{
    /// <summary>
    /// Point-to-point queue where one message is processed by one consumer.
    /// </summary>
    Queue,

    /// <summary>
    /// Pub/sub topic where one message can be delivered to multiple subscribers.
    /// </summary>
    Topic,

    /// <summary>
    /// Ordered event stream/log that can be replayed.
    /// </summary>
    Stream,
}
