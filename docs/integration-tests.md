# Integration tests

`tests/PulumiCrossCloud.Integration.Tests` deploys the library's resources for real, against
[Floci](https://floci.io) cloud emulators, and checks the result with each cloud's own API.
Nothing reaches a real cloud account or Pulumi Cloud: every provider endpoint points at a local
emulator container and each stack uses a throwaway local file backend.

Each cloud gets one fixture that starts its emulator with Testcontainers, runs `pulumi up` on an
inline program built through that cloud's `CloudResourceFactory`, and runs `pulumi destroy` after
the last test. Every suite also previews the deployed program again and expects no changes, which
catches resources that drift on every deploy.

## Running

Requirements: Docker, the [Pulumi CLI](https://www.pulumi.com/docs/install/) on `PATH`, and the
.NET 10 SDK. The first run downloads the Floci images and the Pulumi provider plugins.

```bash
dotnet test tests/PulumiCrossCloud.Integration.Tests
```

Run one cloud with `--filter Cloud=AWS` or `--filter Cloud=GCP`. Every integration test carries
`Category=Integration`, so `dotnet test --filter Category!=Integration` runs only the unit tests.

The `Build and test` GitHub Actions workflow runs the AWS and GCP emulator suites on Linux
with Docker and a pinned Pulumi CLI. A separate job builds the complete solution, including
samples, and runs unit tests on Linux, Windows, and macOS using .NET 10. Package release scripts
run unit tests by default; pass `--include-integration-tests` to `scripts/pack.sh` to include
the emulator suites locally.

The Floci images are pinned in `Infrastructure/FlociEmulator.cs`. Override them with
`FLOCI_AWS_IMAGE` or `FLOCI_GCP_IMAGE` to try a newer release.

## Coverage

| Cloud | Emulator | Covered |
|---|---|---|
| AWS | `floci/floci` | Secrets Manager secret, S3 buckets (versioning, CORS, public read), SNS topics and SQS queues (standard and FIFO, fan-out delivery), Kinesis stream, IAM role with an inline policy, ECR repository, Route 53 zone and record |
| GCP | `floci/floci-gcp` | Cloud Storage buckets (versioning, CORS, public read), Pub/Sub topics, subscriptions and streams (with delivery), Secret Manager secret, service account, API enablement |

The AWS emulator gets the Docker socket because Floci runs ECR's backing registry as a sibling
container. The fixture labels that container for the Testcontainers reaper, so it is removed even
when a run is killed.

Not covered yet, because the emulator cannot host them:

- **Azure.** Last checked against floci-az 0.13.0 and `nightly-09262026` (2026-09-26).
  `floci/floci-az` can't serve the `azure-native` provider yet. The provider signs in
  through MSAL, and floci-az advertises an `http://` issuer while serving HTTPS
  ([floci-io/floci-az#255](https://github.com/floci-io/floci-az/issues/255)). Managed identity
  sign-in also fails, because the provider ignores `msiEndpoint` and calls the real instance
  metadata address instead. floci-az also has no ARM support for Service Bus or Event Hub
  namespaces, which `AzureServiceBusQueue`, `AzureServiceBusTopic` and `AzureEventHubStream` need.
  Storage accounts and blob containers do work over ARM, so `AzureBlobBucket` is the first
  candidate once sign-in works.
- **Compute and databases.** ECS/EKS Fargate, Cloud Run, RDS, Cloud SQL and OpenSearch need
  images, networks or database containers that are out of scope for these suites for now.
