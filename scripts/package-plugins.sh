#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
REPO_DIR="${SCRIPT_DIR%/scripts}"
OUTPUT_DIR="${REPO_DIR}/artifacts/plugins"
VERSION=""
MODE="aot"
RIDS=()
PLUGINS=()
LIST_ONLY="false"

# Every providers/PulumiCrossCloud.Plugin.<Name> project except the shared library is a plugin,
# published as pulumi-resource-crosscloud-<name> (lowercase).
PLUGIN_NAMES=()
PLUGIN_PROJECTS=()
for project_dir in "${REPO_DIR}"/providers/PulumiCrossCloud.Plugin.*/; do
  suffix="$(basename "${project_dir}")"
  suffix="${suffix#PulumiCrossCloud.Plugin.}"
  [[ "${suffix}" == "Common" ]] && continue
  PLUGIN_NAMES+=("$(printf '%s' "${suffix}" | tr '[:upper:]' '[:lower:]')")
  PLUGIN_PROJECTS+=("${project_dir}PulumiCrossCloud.Plugin.${suffix}.csproj")
done

usage() {
  cat <<EOF
Usage: $(basename "$0") --version <semver> --rid <rid> [--rid <rid>...] [options]

Publishes the pulumi-resource-crosscloud-* provider plugins and packages each with the asset
name Pulumi expects for github:// plugin downloads:
  pulumi-resource-crosscloud-<plugin>-v<version>-<os>-<arch>.tar.gz

Options:
  --version <semver>     Plugin version; must match the PulumiCrossCloud package version.
  --rid <rid>            linux-x64, linux-arm64, osx-arm64, win-x64 or win-arm64.
  --plugin <name>        A plugin name such as bird (repeatable; default: all).
  --list                 Print the plugin names, one per line, and exit.
  --framework-dependent  Publish a small single-file executable that needs the .NET 10 runtime
                         instead of a Native AOT binary.
  --output <path>        Output directory (default: artifacts/plugins).

Native AOT needs the platform toolchain and compiles for the host OS only: publish Linux on
Linux, Windows on Windows and macOS (Apple Silicon) on macOS.
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
    --list)
      LIST_ONLY="true"
      shift
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

if [[ "${LIST_ONLY}" == "true" ]]; then
  printf '%s\n' "${PLUGIN_NAMES[@]}"
  exit 0
fi

if [[ -z "${VERSION}" || ${#RIDS[@]} -eq 0 ]]; then
  usage >&2
  exit 1
fi
if [[ ${#PLUGINS[@]} -eq 0 ]]; then
  PLUGINS=("${PLUGIN_NAMES[@]}")
fi

mkdir -p "${OUTPUT_DIR}"
OUTPUT_DIR="$(cd -- "${OUTPUT_DIR}" && pwd)"

for plugin in "${PLUGINS[@]}"; do
  project_file=""
  for index in "${!PLUGIN_NAMES[@]}"; do
    [[ "${PLUGIN_NAMES[${index}]}" == "${plugin}" ]] && project_file="${PLUGIN_PROJECTS[${index}]}"
  done
  if [[ -z "${project_file}" ]]; then
    echo "Unknown plugin: ${plugin} (available: ${PLUGIN_NAMES[*]})" >&2
    exit 1
  fi

  for rid in "${RIDS[@]}"; do
    case "${rid}" in
      linux-x64) platform="linux-amd64" ;;
      linux-arm64) platform="linux-arm64" ;;
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
