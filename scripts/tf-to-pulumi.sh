#!/bin/bash

# Configuration Variables
DEFAULT_LANGUAGE="csharp"
DEFAULT_OUTPUT_DIR="generated-code"
DEFAULT_DOTNET_TARGET_FRAMEWORK="net10.0"
PULUMI_CMD="pulumi"

# Help function
show_help() {
    echo "Usage: $0 -m <module-source> [-o <output-dir>] [-l <language>]"
    echo ""
    echo "Options:"
    echo "  -m  The Terraform/OpenTofu module source (path or URL)."
    echo "  -o  The output directory for the generated Pulumi code (default: ./generated-code)."
    echo "  -l  The target language (csharp, typescript, python, go, java, yaml). Default: csharp."
    echo "  -f  The .NET target framework for generated C# projects. Default: $DEFAULT_DOTNET_TARGET_FRAMEWORK."
    echo "  -s  Extract only the generated SDK directory and remove other project files. Useful when integrating into an existing project."
    echo ""
    echo "Example:"
    echo "  $0 -m ./my-tf-module -o ./src/MyProject -l csharp"
    echo "  $0 -m terraform-aws-modules/vpc/aws -o ./vpc-library"
}

# Parse command line arguments
MODULE_SOURCE=""
OUTPUT_DIR=""
LANGUAGE="$DEFAULT_LANGUAGE"
DOTNET_TARGET_FRAMEWORK="$DEFAULT_DOTNET_TARGET_FRAMEWORK"
SDK_ONLY=false

while getopts "m:o:l:f:sh" opt; do
    case ${opt} in
        m)
            MODULE_SOURCE="$OPTARG"
            ;;
        o)
            OUTPUT_DIR="$OPTARG"
            ;;
        l)
            LANGUAGE="$OPTARG"
            ;;
        f)
            DOTNET_TARGET_FRAMEWORK="$OPTARG"
            ;;
        s)
            SDK_ONLY=true
            ;;
        h)
            show_help
            exit 0
            ;;        \?)
            echo "Invalid option: -$OPTARG" 1>&2
            show_help
            exit 1
            ;; 
        :)
            echo "Option -$OPTARG requires an argument." 1>&2
            show_help
            exit 1
            ;; 
    esac
done

# Validate required arguments
if [ -z "$MODULE_SOURCE" ]; then
    echo "Error: Module source (-m) is required."
    show_help
    exit 1
fi

# Set default output directory if not provided
if [ -z "$OUTPUT_DIR" ]; then
    OUTPUT_DIR="$(pwd)/$DEFAULT_OUTPUT_DIR"
    echo "No output directory specified. Using: $OUTPUT_DIR"
else
    # Resolve relative paths
    if [[ "$OUTPUT_DIR" != /* ]]; then
        OUTPUT_DIR="$(pwd)/$OUTPUT_DIR"
    fi
fi

# Check dependencies
if ! command -v "$PULUMI_CMD" &> /dev/null; then
    echo "Error: 'pulumi' command not found. Please install the Pulumi CLI."
    exit 1
fi

# Check for terraform or tofu (needed for 'pulumi convert' to resolve modules)
if ! command -v terraform &> /dev/null && ! command -v tofu &> /dev/null; then
    echo "Error: Neither 'terraform' nor 'tofu' found. One is required for module resolution."
    exit 1
fi

# Create Output Directory
if [ ! -d "$OUTPUT_DIR" ]; then
    echo "Creating output directory: $OUTPUT_DIR"
    mkdir -p "$OUTPUT_DIR"
fi

echo "========================================================"
echo "Starting Terraform to Pulumi Conversion"
echo "Module:   $MODULE_SOURCE"
echo "Output:   $OUTPUT_DIR"
echo "Language: $LANGUAGE"
echo "========================================================"

# Run pulumi convert
# Note: pulumi convert --from terraform reads from the current directory.
# We must change to the module source directory.

echo "Changing directory to: $MODULE_SOURCE"
pushd "$MODULE_SOURCE" > /dev/null || exit 1

"$PULUMI_CMD" convert \
    --from terraform \
    --language "$LANGUAGE" \
    --out "$OUTPUT_DIR"

CONVERT_EXIT_CODE=$?

popd > /dev/null || exit 1

if [ $CONVERT_EXIT_CODE -eq 0 ]; then
    echo ""
    echo "========================================================"
    echo "Conversion Successful!"
    
    if [ "$SDK_ONLY" = true ]; then
        echo "Extracting SDKs only..."
        if [ -d "$OUTPUT_DIR/sdks" ]; then
            # Move sdks/* to OUTPUT_DIR/ and remove other generated files
            find "$OUTPUT_DIR" -mindepth 1 -maxdepth 1 ! -name 'sdks' -exec rm -rf {} +
            mv "$OUTPUT_DIR/sdks/"* "$OUTPUT_DIR/"
            rm -rf "$OUTPUT_DIR/sdks"
            echo "SDKs extracted to: $OUTPUT_DIR"
        else
            echo "Warning: '-s' flag used but no 'sdks' directory was generated."
        fi
    else
        echo "Code generated in: $OUTPUT_DIR"
    fi

    if [ "$LANGUAGE" = "csharp" ]; then
        echo "Setting generated C# project target framework to: $DOTNET_TARGET_FRAMEWORK"
        find "$OUTPUT_DIR" -name '*.csproj' -print0 |
            while IFS= read -r -d '' PROJECT_FILE; do
                perl -0pi -e "s#<TargetFramework>[^<]+</TargetFramework>#<TargetFramework>$DOTNET_TARGET_FRAMEWORK</TargetFramework>#g" "$PROJECT_FILE"
            done
    fi
    
    echo "========================================================"
else
    echo ""
    echo "========================================================"
    echo "Conversion Failed with exit code $CONVERT_EXIT_CODE"
    echo "Check the error messages above."
    echo "========================================================"
    exit $CONVERT_EXIT_CODE
fi
