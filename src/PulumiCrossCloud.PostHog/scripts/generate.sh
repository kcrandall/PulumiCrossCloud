#!/bin/bash

# Configuration
LANGUAGE="csharp"
PROVIDER_SOURCE="PostHog/posthog"
PROVIDER_VERSION="1.0.13"

# Setup directories
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SHARED_SCRIPT="$SCRIPT_DIR/../../../scripts/tf-to-pulumi.sh"
OUTPUT_DIR="$SCRIPT_DIR/../Generated"
TEMP_TF_DIR="$SCRIPT_DIR/../.temp-tf"

if [ ! -f "$SHARED_SCRIPT" ]; then
    echo "Error: Shared script not found at $SHARED_SCRIPT"
    exit 1
fi

echo "Preparing temporary Terraform configuration for PostHog..."
mkdir -p "$TEMP_TF_DIR"

cat <<EOF > "$TEMP_TF_DIR/main.tf"
terraform {
  required_providers {
    posthog = {
      source  = "$PROVIDER_SOURCE"
      version = "$PROVIDER_VERSION"
    }
  }
}

provider "posthog" {
  api_key         = "placeholder"
  host            = "https://us.posthog.com"
  organization_id = "@current"
  project_id      = "1"
}

resource "posthog_project" "gen" {
  name     = "Generation Trigger"
  timezone = "UTC"
}

resource "posthog_dashboard" "gen" {
  name        = "Generation Dashboard"
  description = "Generation trigger"
}

resource "posthog_feature_flag" "gen" {
  key                = "generation-trigger"
  name               = "Generation Trigger"
  active             = false
  rollout_percentage = 0
}
EOF

echo "Running conversion for PostHog provider..."
"$SHARED_SCRIPT" -m "$TEMP_TF_DIR" -o "$OUTPUT_DIR" -l "$LANGUAGE" -s
EXIT_CODE=$?

if [ $EXIT_CODE -ne 0 ] && [ -d "$OUTPUT_DIR/sdks/posthog" ]; then
    echo "Conversion failed after SDK generation; extracting generated PostHog SDK."
    find "$OUTPUT_DIR" -mindepth 1 -maxdepth 1 ! -name 'sdks' -exec rm -rf {} +
    mv "$OUTPUT_DIR/sdks/"* "$OUTPUT_DIR/"
    rm -rf "$OUTPUT_DIR/sdks"
    EXIT_CODE=0
fi

if [ $EXIT_CODE -ne 0 ] && [ ! -d "$OUTPUT_DIR/posthog" ]; then
    echo "Terraform conversion did not emit an SDK; falling back to pulumi package add."
    PACKAGE_ADD_DIR="$OUTPUT_DIR/.package-add"

    rm -rf "$OUTPUT_DIR"
    mkdir -p "$PACKAGE_ADD_DIR"

    pushd "$PACKAGE_ADD_DIR" > /dev/null || exit 1
    pulumi package add terraform-provider --language dotnet "$PROVIDER_SOURCE" "$PROVIDER_VERSION"
    PACKAGE_ADD_EXIT_CODE=$?
    popd > /dev/null

    if [ -d "$PACKAGE_ADD_DIR/sdks/posthog" ]; then
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

echo "Cleaning up temporary files..."
rm -rf "$TEMP_TF_DIR"

if [ $EXIT_CODE -eq 0 ]; then
    echo "PostHog SDK generation complete."
else
    echo "PostHog SDK generation failed."
    exit $EXIT_CODE
fi
