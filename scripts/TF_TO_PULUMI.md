# Terraform to Pulumi Conversion Script

This directory contains a script to automate the conversion of Terraform or OpenTofu modules into Pulumi code (dotnet, typescript, etc.).

## Script: `tf-to-pulumi.sh`

### Usage

```bash
./tf-to-pulumi.sh -m <module-source> [-o <output-dir>] [-l <language>] [-f <dotnet-target-framework>]
```

### Arguments

| Flag | Description | Required | Default |
|------|-------------|----------|---------|
| `-m` | The Terraform/OpenTofu module source. Can be a local path (e.g., `../modules/vpc`) or a registry URL/Git URL (e.g., `terraform-aws-modules/vpc/aws`). | Yes | - |
| `-o` | The directory where the generated code will be placed. | No | `./generated-code` |
| `-l` | The target language (`csharp`, `typescript`, `python`, `go`, `java`, `yaml`). | No | `csharp` |
| `-f` | The target framework to write into generated C# `.csproj` files. | No | `net10.0` |
| `-s` | Extract only the generated SDK directory and clean up other generated project files. | No | `false` |

### Prerequisites

- **Pulumi CLI**: Must be installed and in your PATH.
- **Terraform** or **OpenTofu**: Must be installed (as `terraform` or `tofu`) to resolve module dependencies.
- **Dotnet SDK**: Required if generating C# code (to build/restore).

### Example

To convert a local Terraform module to C#:

```bash
./tf-to-pulumi.sh -m ./my-terraform-module -o ./src/MyPulumiLibrary -l csharp
```

To convert a module from the Terraform Registry:

```bash
./tf-to-pulumi.sh -m terraform-aws-modules/vpc/aws -o ./generated-vpc -l csharp
```
