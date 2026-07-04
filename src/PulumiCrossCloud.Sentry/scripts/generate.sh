#!/bin/bash

# Configuration
LANGUAGE="csharp"
PROVIDER_SOURCE="jianyuan/sentry"
PROVIDER_VERSION="0.15.4"

# Setup directories
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SHARED_SCRIPT="$SCRIPT_DIR/../../../scripts/tf-to-pulumi.sh"
OUTPUT_DIR="$SCRIPT_DIR/../Generated"
TEMP_TF_DIR="$SCRIPT_DIR/../.temp-tf"

if [ ! -f "$SHARED_SCRIPT" ]; then
    echo "Error: Shared script not found at $SHARED_SCRIPT"
    exit 1
fi

echo "Preparing temporary Terraform configuration for Sentry..."
mkdir -p "$TEMP_TF_DIR"

cat <<EOF > "$TEMP_TF_DIR/main.tf"
terraform {
  required_providers {
    sentry = {
      source  = "$PROVIDER_SOURCE"
      version = "$PROVIDER_VERSION"
    }
  }
}

provider "sentry" {
  token = "placeholder"
}

resource "sentry_organization" "gen" {
  name        = "Generation Trigger"
  slug        = "generation-trigger"
  agree_terms = true
}
EOF

echo "Running conversion for Sentry provider..."
"$SHARED_SCRIPT" -m "$TEMP_TF_DIR" -o "$OUTPUT_DIR" -l "$LANGUAGE" -s
EXIT_CODE=$?

if [ $EXIT_CODE -ne 0 ] && [ -d "$OUTPUT_DIR/sdks/sentry" ]; then
    echo "Conversion failed after SDK generation; extracting generated Sentry SDK."
    find "$OUTPUT_DIR" -mindepth 1 -maxdepth 1 ! -name 'sdks' -exec rm -rf {} +
    mv "$OUTPUT_DIR/sdks/"* "$OUTPUT_DIR/"
    rm -rf "$OUTPUT_DIR/sdks"
    EXIT_CODE=0
fi

if [ $EXIT_CODE -ne 0 ] && [ ! -d "$OUTPUT_DIR/sentry" ]; then
    echo "Terraform conversion did not emit an SDK; falling back to pulumi package add."
    PACKAGE_ADD_DIR="$OUTPUT_DIR/.package-add"

    rm -rf "$OUTPUT_DIR"
    mkdir -p "$PACKAGE_ADD_DIR"

    pushd "$PACKAGE_ADD_DIR" > /dev/null || exit 1
    pulumi package add terraform-provider --language dotnet "$PROVIDER_SOURCE" "$PROVIDER_VERSION"
    PACKAGE_ADD_EXIT_CODE=$?
    popd > /dev/null

    if [ -d "$PACKAGE_ADD_DIR/sdks/sentry" ]; then
        mkdir -p "$OUTPUT_DIR"
        mv "$PACKAGE_ADD_DIR/sdks/"* "$OUTPUT_DIR/"
        rm -rf "$PACKAGE_ADD_DIR"
        EXIT_CODE=0
    else
        EXIT_CODE=$PACKAGE_ADD_EXIT_CODE
    fi
fi

if [ $EXIT_CODE -eq 0 ]; then
    echo "Setting generated C# project target framework to net10.0..."
    find "$OUTPUT_DIR" -name '*.csproj' -print0 |
        while IFS= read -r -d '' PROJECT_FILE; do
            perl -0pi -e "s#<TargetFramework>[^<]+</TargetFramework>#<TargetFramework>net10.0</TargetFramework>#g" "$PROJECT_FILE"
        done
fi

if [ $EXIT_CODE -eq 0 ] && [ -f "$OUTPUT_DIR/sentry/Plugin.cs" ]; then
    echo "Patching generated Sentry plugin property name collision..."
    perl -0pi -e 's/public Output<string> Plugin \{ get; private set; \}/public Output<string> PluginValue { get; private set; }/g' "$OUTPUT_DIR/sentry/Plugin.cs"
fi

echo "Cleaning up temporary files..."
rm -rf "$TEMP_TF_DIR"

if [ $EXIT_CODE -eq 0 ]; then
    echo "Sentry SDK generation complete."
else
    echo "Sentry SDK generation failed."
    exit $EXIT_CODE
fi
