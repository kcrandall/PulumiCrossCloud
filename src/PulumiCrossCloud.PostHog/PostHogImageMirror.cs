using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.PostHog;

internal static class PostHogImageMirror
{
    public static IContainerImage Push(
        CloudResourceFactory factory,
        IContainerRegistry registry,
        string name,
        string imageName,
        string imageTag
    )
    {
        var image = factory.CreateContainerImage(name, registry);
        var dockerfilePath = WriteDockerfile(imageName, imageTag);
        image.PushImage(dockerfilePath, Path.GetDirectoryName(dockerfilePath)!);
        return image;
    }

    private static string WriteDockerfile(string imageName, string imageTag)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "pulumi-cross-cloud-posthog",
            Guid.NewGuid().ToString("N")
        );
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
