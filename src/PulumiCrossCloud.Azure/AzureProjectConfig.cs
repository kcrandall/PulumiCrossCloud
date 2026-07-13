using Pulumi;

namespace PulumiCrossCloud.Azure;

public static class AzureProjectConfig
{
    private static string? _resourceGroupName;
    private static string? _serviceBusNamespace;
    private static string? _eventHubNamespace;
    private static string? _region;

    public static string ResourceGroupName
    {
        get
        {
            if (!string.IsNullOrEmpty(_resourceGroupName))
                return _resourceGroupName;
            var config = new Config("azure-native");
            return config.Get("resourceGroupName") ?? string.Empty;
        }
        set => _resourceGroupName = value;
    }

    public static string ServiceBusNamespace
    {
        get => _serviceBusNamespace ?? string.Empty;
        set => _serviceBusNamespace = value;
    }

    public static string EventHubNamespace
    {
        get => _eventHubNamespace ?? string.Empty;
        set => _eventHubNamespace = value;
    }

    public static string Region
    {
        get
        {
            if (!string.IsNullOrEmpty(_region))
                return _region;
            var config = new Config("azure-native");
            return config.Get("location") ?? "eastus";
        }
        set => _region = value;
    }

    public static void Reset()
    {
        _resourceGroupName = null;
        _serviceBusNamespace = null;
        _eventHubNamespace = null;
        _region = null;
    }
}
