using Pulumi;

namespace PulumiCrossCloud.Core;

/// <summary>
/// Permissions for messaging resources.
/// </summary>
public enum MessagingPermissions
{
    /// <summary>
    /// Permission to send messages to the endpoint.
    /// </summary>
    Send,

    /// <summary>
    /// Permission to receive/consume messages from the endpoint.
    /// </summary>
    Receive,

    /// <summary>
    /// Full administrative permissions.
    /// </summary>
    Admin,
}

/// <summary>
/// Base interface for all messaging endpoints.
/// </summary>
public interface IMessagingEndpoint : ICloudResource, IPolicyProvider<MessagingPermissions>
{
    /// <summary>
    /// The specific kind of messaging resource (Queue, Topic, Stream).
    /// </summary>
    MessagingKind Kind { get; }

    /// <summary>
    /// The primary endpoint or ID used to SEND messages to this resource.
    /// </summary>
    Output<string> SendEndpoint { get; }
}

/// <summary>
/// Interface for a point-to-point work queue.
/// </summary>
public interface IQueueEndpoint : IMessagingEndpoint
{
    /// <summary>
    /// The endpoint or ID used to RECEIVE/LISTEN for messages from this queue.
    /// </summary>
    Output<string> ReceiveEndpoint { get; }

    /// <summary>
    /// The maximum number of delivery attempts before moving to a dead-letter queue.
    /// </summary>
    Output<int?> MaxDeliveryAttempts { get; }

    /// <summary>
    /// The amount of time a message is invisible to other consumers after being picked up.
    /// </summary>
    Output<int?> VisibilityTimeoutSeconds { get; }

    /// <summary>
    /// The ARN or Resource ID of the dead-letter target.
    /// </summary>
    Output<string?> DeadLetterTarget { get; }
}

/// <summary>
/// Interface for a fan-out pub/sub topic.
/// </summary>
public interface ITopicEndpoint : IMessagingEndpoint
{
    /// <summary>
    /// Whether the topic supports message ordering (FIFO).
    /// </summary>
    Output<bool> SupportsOrdering { get; }
}

/// <summary>
/// Interface for a replayable event stream.
/// </summary>
public interface IStreamEndpoint : IMessagingEndpoint
{
    /// <summary>
    /// The endpoint or ID used to RECEIVE/LISTEN for messages from this stream.
    /// </summary>
    Output<string> ReceiveEndpoint { get; }

    /// <summary>
    /// Data retention period in hours.
    /// </summary>
    Output<int?> RetentionHours { get; }

    /// <summary>
    /// Abstracted throughput units (e.g. Shards in Kinesis, Partitions in Kafka/Hubs).
    /// </summary>
    Output<int?> ThroughputUnits { get; }
}
