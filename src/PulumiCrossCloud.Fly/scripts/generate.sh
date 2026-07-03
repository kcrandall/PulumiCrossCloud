#!/bin/bash

# Configuration
LANGUAGE="csharp"
PROVIDER_SOURCE="stategraph/fly"
PROVIDER_VERSION="0.2.3"

# Setup directories
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SHARED_SCRIPT="$SCRIPT_DIR/../../../scripts/tf-to-pulumi.sh"
OUTPUT_DIR="$SCRIPT_DIR/../Generated"
TEMP_TF_DIR="$SCRIPT_DIR/../.temp-tf"

if [ ! -f "$SHARED_SCRIPT" ]; then
    echo "Error: Shared script not found at $SHARED_SCRIPT"
    exit 1
fi

echo "Preparing temporary Terraform configuration for Fly.io..."
mkdir -p "$TEMP_TF_DIR"

cat <<EOF > "$TEMP_TF_DIR/main.tf"
terraform {
  required_providers {
    fly = {
      source  = "$PROVIDER_SOURCE"
      version = "$PROVIDER_VERSION"
    }
  }
}

provider "fly" {
  api_token = "placeholder"
  org_slug  = "personal"
}

resource "fly_app" "gen" {
  name     = "generation-trigger"
  org_slug = "personal"
}

resource "fly_machine" "gen" {
  app    = fly_app.gen.name
  region = "iad"
  name   = "generation-trigger"
  image  = "nginx:latest"
}
EOF

echo "Running conversion for Fly.io provider..."
"$SHARED_SCRIPT" -m "$TEMP_TF_DIR" -o "$OUTPUT_DIR" -l "$LANGUAGE" -s
EXIT_CODE=$?

if [ $EXIT_CODE -ne 0 ] && [ -d "$OUTPUT_DIR/sdks/fly" ]; then
    echo "Conversion failed after SDK generation; extracting generated Fly SDK."
    find "$OUTPUT_DIR" -mindepth 1 -maxdepth 1 ! -name 'sdks' -exec rm -rf {} +
    mv "$OUTPUT_DIR/sdks/"* "$OUTPUT_DIR/"
    rm -rf "$OUTPUT_DIR/sdks"
    EXIT_CODE=0
fi

if [ $EXIT_CODE -eq 0 ] && [ -f "$OUTPUT_DIR/fly/Token.cs" ]; then
    echo "Patching generated Fly token property name collision..."
    perl -0pi -e 's/public Output<string> Token \{ get; private set; \}/public Output<string> TokenValue { get; private set; }/g; s/public Input<string>\\? Token\\n        \\{/public Input<string>? TokenValue\n        {/g' "$OUTPUT_DIR/fly/Token.cs"
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
    echo "Fly.io SDK generation complete."
else
    echo "Fly.io SDK generation failed."
    exit $EXIT_CODE
fi
