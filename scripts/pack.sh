#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
REPO_DIR="${SCRIPT_DIR%/scripts}"
SOLUTION_FILE="${REPO_DIR}/PulumiCrossCloud.slnx"
OUTPUT_DIR="${REPO_DIR}/artifacts/nuget"
CONFIGURATION="Release"
RUN_TESTS=true
VERSION_OVERRIDE=""
PRERELEASE_CHANNEL="alpha"

usage() {
  cat <<EOF
Usage: $(basename "$0") [options]

Options:
  --version <semver>      Override package version for all projects (lockstep).
  --prerelease <channel>  Prerelease channel: alpha (default), beta, stable.
  --configuration <name>  Build configuration (default: Release).
  --skip-tests            Skip dotnet test before packing.
  --output <path>         Output directory for .nupkg files.
EOF
}

while (($# > 0)); do
  case "$1" in
    --version)
      VERSION_OVERRIDE="${2:-}"
      shift 2
      ;;
    --prerelease)
      PRERELEASE_CHANNEL="${2:-}"
      shift 2
      ;;
    --configuration)
      CONFIGURATION="${2:-}"
      shift 2
      ;;
    --skip-tests)
      RUN_TESTS=false
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
      echo "Unknown argument: $1"
      usage
      exit 1
      ;;
  esac
done

if [[ ! -f "${SOLUTION_FILE}" ]]; then
  echo "Solution file not found: ${SOLUTION_FILE}"
  exit 1
fi

RESOLVED_VERSION="$("${SCRIPT_DIR}/resolve-version.sh" --version "${VERSION_OVERRIDE}" --prerelease "${PRERELEASE_CHANNEL}")"
MSBUILD_VERSION_ARG=("-p:Version=${RESOLVED_VERSION}")

echo "Packing lockstep version: ${RESOLVED_VERSION}"

mkdir -p "${OUTPUT_DIR}"
rm -f "${OUTPUT_DIR}"/*.nupkg "${OUTPUT_DIR}"/*.snupkg

dotnet restore "${SOLUTION_FILE}"
dotnet build "${SOLUTION_FILE}" \
  --configuration "${CONFIGURATION}" \
  --no-restore \
  -p:ContinuousIntegrationBuild=true \
  "${MSBUILD_VERSION_ARG[@]}"

if [[ "${RUN_TESTS}" == "true" ]]; then
  dotnet test "${SOLUTION_FILE}" \
    --configuration "${CONFIGURATION}" \
    --no-build \
    --nologo \
    --verbosity minimal \
    "${MSBUILD_VERSION_ARG[@]}"
fi

PACKABLE_PROJECTS=()
while IFS= read -r project; do
  PACKABLE_PROJECTS+=("${project}")
done < <(find "${REPO_DIR}/src" -mindepth 2 -maxdepth 2 -type f -name 'PulumiCrossCloud.*.csproj' | sort)

if ((${#PACKABLE_PROJECTS[@]} == 0)); then
  echo "No packable projects were found under ${REPO_DIR}/src"
  exit 1
fi

for project in "${PACKABLE_PROJECTS[@]}"; do
  dotnet pack "${project}" \
    --configuration "${CONFIGURATION}" \
    --no-build \
    --output "${OUTPUT_DIR}" \
    -p:ContinuousIntegrationBuild=true \
    "${MSBUILD_VERSION_ARG[@]}"
done

echo "Packages created in ${OUTPUT_DIR}"
