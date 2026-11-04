#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
REPO_DIR="${SCRIPT_DIR%/scripts}"
OUTPUT_DIR="${REPO_DIR}/artifacts/plugins"
VERSION=""
MODE="aot"
RIDS=()
PLUGINS=()
ALL_PLUGINS=(bird posthog mailgun)

usage() {
  cat <<EOF
Usage: $(basename "$0") --version <semver> --rid <rid> [--rid <rid>...] [options]

Publishes the pulumi-resource-crosscloud-* provider plugins and packages each with the asset
name Pulumi expects for github:// plugin downloads:
  pulumi-resource-crosscloud-<plugin>-v<version>-<os>-<arch>.tar.gz

Options:
  --version <semver>     Plugin version; must match the PulumiCrossCloud package version.
  --rid <rid>            linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64 or win-arm64.
  --plugin <name>        bird, posthog or mailgun (repeatable; default: all).
  --framework-dependent  Publish a small single-file executable that needs the .NET 10 runtime
                         instead of a Native AOT binary.
  --output <path>        Output directory (default: artifacts/plugins).

Native AOT needs the platform toolchain and compiles for the host OS only: publish Linux on
Linux, Windows on Windows and macOS on macOS (either architecture on macOS).
EOF
}

while (($# > 0)); do
  case "$1" in
    --version)
      VERSION="${2:-}"
      shift 2
      ;;
    --rid)
      RIDS+=("${2:-}")
      shift 2
      ;;
    --plugin)
      PLUGINS+=("${2:-}")
      shift 2
      ;;
    --framework-dependent)
      MODE="framework"
      shift
      ;;
    --output)
      OUTPUT_DIR="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "${VERSION}" || ${#RIDS[@]} -eq 0 ]]; then
  usage >&2
  exit 1
fi
if [[ ${#PLUGINS[@]} -eq 0 ]]; then
  PLUGINS=("${ALL_PLUGINS[@]}")
fi

mkdir -p "${OUTPUT_DIR}"
OUTPUT_DIR="$(cd -- "${OUTPUT_DIR}" && pwd)"

for plugin in "${PLUGINS[@]}"; do
  case "${plugin}" in
    bird) project="Bird" ;;
    posthog) project="PostHog" ;;
    mailgun) project="Mailgun" ;;
    *)
      echo "Unknown plugin: ${plugin}" >&2
      exit 1
      ;;
  esac
  project_file="${REPO_DIR}/providers/PulumiCrossCloud.Plugin.${project}/PulumiCrossCloud.Plugin.${project}.csproj"

  for rid in "${RIDS[@]}"; do
    case "${rid}" in
      linux-x64) platform="linux-amd64" ;;
      linux-arm64) platform="linux-arm64" ;;
      osx-x64) platform="darwin-amd64" ;;
      osx-arm64) platform="darwin-arm64" ;;
      win-x64) platform="windows-amd64" ;;
      win-arm64) platform="windows-arm64" ;;
      *)
        echo "Unsupported runtime identifier: ${rid}" >&2
        exit 1
        ;;
    esac

    executable="pulumi-resource-crosscloud-${plugin}"
    [[ "${rid}" == win-* ]] && executable="${executable}.exe"

    if [[ "${MODE}" == "aot" ]]; then
      mode_args=(-p:PublishAot=true -p:StripSymbols=true)
    else
      # Single-file compression is deliberately off: compressed bundles crashed in
      # HttpConnectionPool with AccessViolationException under concurrent requests.
      mode_args=(--self-contained false -p:PublishSingleFile=true)
    fi

    publish_dir="$(mktemp -d)"
    dotnet publish "${project_file}" \
      --configuration Release \
      --runtime "${rid}" \
      --output "${publish_dir}" \
      -p:DebugType=none \
      -p:ContinuousIntegrationBuild=true \
      "-p:Version=${VERSION}" \
      "${mode_args[@]}"

    if [[ ! -f "${publish_dir}/${executable}" ]]; then
      echo "Publish did not produce ${executable} for ${rid}" >&2
      exit 1
    fi

    archive="${OUTPUT_DIR}/pulumi-resource-crosscloud-${plugin}-v${VERSION}-${platform}.tar.gz"
    tar -czf "${archive}" -C "${publish_dir}" "${executable}"
    rm -rf "${publish_dir}"
    echo "Packaged ${archive}"
  done
done
