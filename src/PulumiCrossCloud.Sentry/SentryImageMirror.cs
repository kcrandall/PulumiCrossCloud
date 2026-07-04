using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Sentry;

internal static class SentryImageMirror
{
    public static IContainerImage Push(
        CloudResourceFactory factory,
        IContainerRegistry registry,
        string name,
        string sourceImage,
        string sourceTag
    )
    {
        var image = factory.CreateContainerImage(name, registry);
        var dockerfilePath = WriteDockerfile(sourceImage, sourceTag);
        image.PushImage(dockerfilePath, Path.GetDirectoryName(dockerfilePath)!);
        return image;
    }

    private static string WriteDockerfile(string imageName, string imageTag)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pulumi-cross-cloud-sentry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var dockerfilePath = Path.Combine(directory, "Dockerfile");
        File.WriteAllText(
            dockerfilePath,
            $"""
            FROM {imageName}:{imageTag}
            """
        );

        return dockerfilePath;
    }
}
