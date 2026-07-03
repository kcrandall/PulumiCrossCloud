#!/bin/bash

# Configuration
LANGUAGE="csharp"
PROVIDER_SOURCE="upstash/upstash"
PROVIDER_VERSION="2.1.0"

# Setup directories
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SHARED_SCRIPT="$SCRIPT_DIR/../../../scripts/tf-to-pulumi.sh"
OUTPUT_DIR="$SCRIPT_DIR/../Generated"
TEMP_TF_DIR="$SCRIPT_DIR/../.temp-tf"

if [ ! -f "$SHARED_SCRIPT" ]; then
    echo "Error: Shared script not found at $SHARED_SCRIPT"
    exit 1
fi

echo "Preparing temporary Terraform configuration for Upstash..."
mkdir -p "$TEMP_TF_DIR"

cat <<EOF > "$TEMP_TF_DIR/main.tf"
terraform {
  required_providers {
    upstash = {
      source  = "$PROVIDER_SOURCE"
      version = "$PROVIDER_VERSION"
    }
  }
}

# Provider configuration placeholder
provider "upstash" {
  email   = "placeholder"
  api_key = "placeholder"
}

# Minimal resource to trigger SDK generation
resource "upstash_redis_database" "gen" {
  database_name = "generation-trigger"
  region        = "eu-west-1"
  tls           = true
}
EOF

echo "Running conversion for Upstash provider..."
"$SHARED_SCRIPT" -m "$TEMP_TF_DIR" -o "$OUTPUT_DIR" -l "$LANGUAGE" -s

EXIT_CODE=$?

# Cleanup
echo "Cleaning up temporary files..."
rm -rf "$TEMP_TF_DIR"

if [ $EXIT_CODE -eq 0 ]; then
    echo "Upstash SDK generation complete."
else
    echo "Upstash SDK generation failed."
    exit $EXIT_CODE
fi
