using System.Net.Http.Headers;
using System.Text;

namespace PulumiCrossCloud.Integration.Tests.Gcp;

[Trait("Category", "Integration")]
[Trait("Cloud", "GCP")]
public sealed class GcpFlociTests(GcpFlociFixture fixture) : IClassFixture<GcpFlociFixture>
{
    [Fact]
    public async Task Redeploying_the_same_program_changes_nothing()
    {
        var preview = await fixture.Stack.PreviewAsync();

        // floci-gcp does not store a bucket's iamConfiguration, so uniform bucket-level access
        // always reads back as disabled.
        Assert.True(preview.UnexpectedSteps("uniformBucketLevelAccess").Count == 0, preview.Diff);
    }

    [Fact]
    public async Task Bucket_has_versioning_and_cors()
    {
        var bucket = await fixture.GetJsonAsync($"/storage/v1/b/{GcpFlociFixture.BucketName}");

        Assert.Equal(
            GcpFlociFixture.Region,
            bucket["location"]!.GetValue<string>(),
            ignoreCase: true
        );
        Assert.True(bucket["versioning"]!["enabled"]!.GetValue<bool>());
        var cors = Assert.Single(bucket["cors"]!.AsArray())!;
        Assert.Equal(["https://app.floci-it.test"], Strings(cors["origin"]));
        Assert.Equal(["GET", "PUT"], Strings(cors["method"]));
        Assert.Equal(["ETag"], Strings(cors["responseHeader"]));
        Assert.Equal(600, cors["maxAgeSeconds"]!.GetValue<int>());
    }

    [Fact]
    public async Task Public_bucket_grants_all_users_object_viewer()
    {
        var policy = await fixture.GetJsonAsync(
            $"/storage/v1/b/{GcpFlociFixture.PublicBucketName}/iam"
        );

        Assert.Contains(
            policy["bindings"]!.AsArray(),
            binding =>
                binding!["role"]!.GetValue<string>() == "roles/storage.objectViewer"
                && Strings(binding["members"]).Contains("allUsers")
        );
    }

    [Fact]
    public async Task Bucket_round_trips_objects()
    {
        // The unversioned bucket, so deleting the object leaves the bucket empty for destroy.
        const string bucket = GcpFlociFixture.PublicBucketName;
        var upload = new StringContent("hello from floci");
        upload.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using (
            var uploaded = await fixture.Http.PostAsync(
                $"/upload/storage/v1/b/{bucket}/o?uploadType=media&name=hello.txt",
                upload
            )
        )
            uploaded.EnsureSuccessStatusCode();
        try
        {
            var content = await fixture.Http.GetStringAsync(
                $"/storage/v1/b/{bucket}/o/hello.txt?alt=media"
            );

            Assert.Equal("hello from floci", content);
        }
        finally
        {
            using var deleted = await fixture.Http.DeleteAsync(
                $"/storage/v1/b/{bucket}/o/hello.txt"
            );
        }
    }

    [Fact]
    public async Task Queue_subscribes_to_the_shared_topic()
    {
        var subscription = await fixture.GetJsonAsync(
            $"/v1/{fixture.Stack.Output("queueSubscriptionId")}"
        );

        Assert.Equal(fixture.Stack.Output("topicId"), subscription["topic"]!.GetValue<string>());
        Assert.Equal(
            GcpFlociFixture.QueueAckDeadlineSeconds,
            subscription["ackDeadlineSeconds"]!.GetValue<int>()
        );
    }

    [Fact]
    public async Task Topic_fans_out_to_the_subscribed_queue()
    {
        var message = $"fan-out-{Guid.NewGuid()}";

        await PublishAsync(fixture.Stack.Output("topicId"), message);

        Assert.Contains(message, await PullAsync(fixture.Stack.Output("queueSubscriptionId")));
    }

    [Fact]
    public async Task Standalone_queue_delivers_through_its_private_topic()
    {
        var message = $"standalone-{Guid.NewGuid()}";

        await PublishAsync(fixture.Stack.Output("standaloneTopicId"), message);

        Assert.Contains(message, await PullAsync(fixture.Stack.Output("standaloneSubscriptionId")));
    }

    [Fact]
    public async Task Stream_topic_retains_messages_for_the_requested_duration()
    {
        var topic = await fixture.GetJsonAsync($"/v1/{fixture.Stack.Output("streamTopicId")}");

        Assert.Equal(
            $"{GcpFlociFixture.StreamRetentionHours * 3600}s",
            topic["messageRetentionDuration"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task Secret_stores_its_value()
    {
        var version = await fixture.GetJsonAsync(
            $"/v1/{fixture.Stack.Output("secretName")}/versions/latest:access"
        );
        var data = Convert.FromBase64String(version["payload"]!["data"]!.GetValue<string>());

        Assert.Equal(GcpFlociFixture.SecretValue, Encoding.UTF8.GetString(data));
    }

    [Fact]
    public async Task Required_apis_are_enabled_for_the_project()
    {
        foreach (var api in new[] { "secretmanager.googleapis.com", "iam.googleapis.com" })
        {
            var service = await fixture.GetJsonAsync(
                $"/v1/projects/{GcpFlociFixture.Project}/services/{api}"
            );

            Assert.Equal("ENABLED", service["state"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Role_is_a_service_account()
    {
        var email = fixture.Stack.Output("serviceAccountEmail");

        var account = await fixture.GetJsonAsync(
            $"/v1/projects/{GcpFlociFixture.Project}/serviceAccounts/{email}"
        );

        Assert.StartsWith($"{GcpFlociFixture.ServiceAccountId}@", email);
        Assert.Equal(email, account["email"]!.GetValue<string>());
        Assert.Equal(GcpFlociFixture.ServiceAccountId, account["displayName"]!.GetValue<string>());
    }

    private Task PublishAsync(string topicId, string message) =>
        fixture.PostJsonAsync(
            $"/v1/{topicId}:publish",
            new
            {
                messages = new[]
                {
                    new { data = Convert.ToBase64String(Encoding.UTF8.GetBytes(message)) },
                },
            }
        );

    private async Task<IReadOnlyList<string>> PullAsync(string subscriptionId)
    {
        var pulled = await fixture.PostJsonAsync(
            $"/v1/{subscriptionId}:pull",
            new { maxMessages = 10 }
        );

        return (pulled["receivedMessages"]?.AsArray() ?? [])
            .Select(received => received!["message"]!["data"]!.GetValue<string>())
            .Select(data => Encoding.UTF8.GetString(Convert.FromBase64String(data)))
            .ToList();
    }

    private static List<string> Strings(System.Text.Json.Nodes.JsonNode? array) =>
        array!.AsArray().Select(item => item!.GetValue<string>()).ToList();
}
