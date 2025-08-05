# PulumiCrossCloud.Gcp

This project provides GCP (Google Cloud Platform) resource implementations for the PulumiCrossCloud library, enabling cloud-agnostic infrastructure as code using Pulumi and .NET.

## Getting Started: Setting Up GCP for Pulumi

If you've never used GCP before, follow these steps to set up authentication and permissions for Pulumi deployments:

### 1. Create a Google Cloud Project
- Go to [Google Cloud Console](https://console.cloud.google.com/)
- Click the project dropdown (top left) and select "New Project"
- Give your project a name and click "Create"

### 2. Create a Service Account
- Go to **IAM & Admin > Service Accounts**
- Click "Create Service Account"
- Name it (e.g., `pulumi-admin`)
- Click "Create and Continue"
- Grant these roles:
  - **Owner** (for full admin access, recommended for initial setup)
  - Or, for least privilege, add:
    - Cloud Run Admin
    - Artifact Registry Admin
    - Secret Manager Admin
    - Service Account Admin
- Click "Done"

### 3. Create and Download a Service Account Key
- In the Service Accounts list, click your new account
- Go to the **Keys** tab
- Click "Add Key" > "Create new key"
- Choose **JSON** and click "Create"
- Download the JSON file and save it securely (e.g., as `gcp-pulumi-key.json`)

### 4. Set Up Authentication for Pulumi
- Install the [Google Cloud SDK](https://cloud.google.com/sdk/docs/install) (optional, for CLI access)
- Set the environment variable for Pulumi:
  ```sh
  export GOOGLE_APPLICATION_CREDENTIALS=/path/to/gcp-pulumi-key.json
  ```
- Or, configure Pulumi directly in your stack config:
  ```yaml
  config:
    gcp:project: <your-project-id>
    gcp:region: <your-region>
    gcp:credentials: /path/to/gcp-pulumi-key.json
  ```

### 5. Install .NET and Pulumi CLI
- [.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- [Pulumi CLI](https://www.pulumi.com/docs/get-started/install/)

### 6. Run the Sample
- Navigate to the sample project:
  ```sh
  cd samples/PulumiCrossCloud.Samples.Gcp
  pulumi up
  ```

## Troubleshooting
- Make sure your service account has the correct roles and the key file path is correct.
- If you see permission errors, check IAM roles and API enablement.
- For more details, see [Pulumi GCP Provider Docs](https://www.pulumi.com/registry/packages/gcp/)

## Resources
- [Google Cloud Console](https://console.cloud.google.com/)
- [Pulumi GCP Provider](https://www.pulumi.com/registry/packages/gcp/)
- [Pulumi Documentation](https://www.pulumi.com/docs/)

---

**PulumiCrossCloud** makes it easy to deploy the same infrastructure code to AWS, Azure, or GCP. Just switch the factory and run your stack!
