#!/bin/bash

# Configuration
LANGUAGE="csharp"
PROVIDER_SOURCE="koyeb/koyeb"
PROVIDER_VERSION="0.1.11"

# Setup directories
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SHARED_SCRIPT="$SCRIPT_DIR/../../../scripts/tf-to-pulumi.sh"
OUTPUT_DIR="$SCRIPT_DIR/../Generated"
TEMP_TF_DIR="$SCRIPT_DIR/../.temp-tf"

if [ ! -f "$SHARED_SCRIPT" ]; then
    echo "Error: Shared script not found at $SHARED_SCRIPT"
    exit 1
fi

echo "Preparing temporary Terraform configuration for Koyeb..."
mkdir -p "$TEMP_TF_DIR"

cat <<EOF > "$TEMP_TF_DIR/main.tf"
terraform {
  required_providers {
    koyeb = {
      source  = "$PROVIDER_SOURCE"
      version = "$PROVIDER_VERSION"
    }
  }
}

provider "koyeb" {
  token = "placeholder"
}

resource "koyeb_app" "gen" {
  name = "generation-trigger"
}

resource "koyeb_service" "gen" {
  app_name = koyeb_app.gen.name

  definition {
    name = "generation-trigger"

    instance_types {
      type = "micro"
    }

    ports {
      port     = 3000
      protocol = "http"
    }

    regions = ["was"]

    scalings {
      min = 1
      max = 1
    }

    docker {
      image = "nginx:latest"
    }
  }
}
EOF

echo "Running conversion for Koyeb provider..."
"$SHARED_SCRIPT" -m "$TEMP_TF_DIR" -o "$OUTPUT_DIR" -l "$LANGUAGE" -s
EXIT_CODE=$?

if [ $EXIT_CODE -ne 0 ] && [ -d "$OUTPUT_DIR/sdks/koyeb" ]; then
    echo "Conversion failed after SDK generation; extracting generated Koyeb SDK."
    find "$OUTPUT_DIR" -mindepth 1 -maxdepth 1 ! -name 'sdks' -exec rm -rf {} +
    mv "$OUTPUT_DIR/sdks/"* "$OUTPUT_DIR/"
    rm -rf "$OUTPUT_DIR/sdks"
    EXIT_CODE=0
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
    echo "Koyeb SDK generation complete."
else
    echo "Koyeb SDK generation failed."
    exit $EXIT_CODE
fi
