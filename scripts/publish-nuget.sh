#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
REPO_DIR="${SCRIPT_DIR%/scripts}"
PACKAGE_DIR="${REPO_DIR}/artifacts/nuget"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
DRY_RUN=false

usage() {
  cat <<EOF
Usage: $(basename "$0") [options]

Options:
  --package-dir <path>  Directory containing .nupkg files (default: artifacts/nuget).
  --source <url>        NuGet feed source URL (default: nuget.org v3).
  --dry-run             Print packages that would be pushed.
EOF
}

while (($# > 0)); do
  case "$1" in
    --package-dir)
      PACKAGE_DIR="${2:-}"
      shift 2
      ;;
    --source)
      NUGET_SOURCE="${2:-}"
      shift 2
      ;;
    --dry-run)
      DRY_RUN=true
      shift
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

if [[ ! -d "${PACKAGE_DIR}" ]]; then
  echo "Package directory not found: ${PACKAGE_DIR}"
  exit 1
fi

PACKAGES=()
while IFS= read -r package; do
  PACKAGES+=("${package}")
done < <(find "${PACKAGE_DIR}" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.symbols.nupkg' | sort)

if ((${#PACKAGES[@]} == 0)); then
  echo "No .nupkg files found in ${PACKAGE_DIR}"
  exit 1
fi

if [[ "${DRY_RUN}" == "true" ]]; then
  printf 'Would publish:\n'
  printf ' - %s\n' "${PACKAGES[@]}"
  exit 0
fi

if [[ -z "${NUGET_API_KEY:-}" ]]; then
  echo "NUGET_API_KEY environment variable is required."
  exit 1
fi

for package in "${PACKAGES[@]}"; do
  echo "Publishing ${package}"
  dotnet nuget push "${package}" \
    --api-key "${NUGET_API_KEY}" \
    --source "${NUGET_SOURCE}" \
    --skip-duplicate
done
