#!/bin/bash

# Configuration
LANGUAGE="csharp"
PROVIDER_SOURCE="kislerdm/neon"
PROVIDER_VERSION="0.6.3"

# Setup directories
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SHARED_SCRIPT="$SCRIPT_DIR/../../../scripts/tf-to-pulumi.sh"
OUTPUT_DIR="$SCRIPT_DIR/../Generated"
TEMP_TF_DIR="$SCRIPT_DIR/../.temp-tf"

if [ ! -f "$SHARED_SCRIPT" ]; then
    echo "Error: Shared script not found at $SHARED_SCRIPT"
    exit 1
fi

echo "Preparing temporary Terraform configuration for Neon..."
mkdir -p "$TEMP_TF_DIR"

cat <<EOF > "$TEMP_TF_DIR/main.tf"
terraform {
  required_providers {
    neon = {
      source  = "$PROVIDER_SOURCE"
      version = "$PROVIDER_VERSION"
    }
  }
}

# Provider configuration placeholder
provider "neon" {
  api_key = "placeholder"
}

# Minimal resource to trigger SDK generation
resource "neon_project" "gen" {
  name = "generation-trigger"
}
EOF

echo "Running conversion for Neon provider..."
"$SHARED_SCRIPT" -m "$TEMP_TF_DIR" -o "$OUTPUT_DIR" -l "$LANGUAGE" -s

EXIT_CODE=$?

# Cleanup
echo "Cleaning up temporary files..."
rm -rf "$TEMP_TF_DIR"

if [ $EXIT_CODE -eq 0 ]; then
    echo "Neon SDK generation complete."
else
    echo "Neon SDK generation failed."
    exit $EXIT_CODE
fi