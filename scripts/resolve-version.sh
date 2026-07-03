#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
REPO_DIR="${SCRIPT_DIR%/scripts}"
BUILD_PROPS_FILE="${REPO_DIR}/Directory.Build.props"
INPUT_VERSION=""
PRERELEASE_CHANNEL="alpha"

usage() {
  cat <<EOF
Usage: $(basename "$0") [options]

Options:
  --version <semver>                 Explicit base version to use.
  --prerelease <stable|alpha|beta>  Optional prerelease channel suffix (default: alpha).
EOF
}

while (($# > 0)); do
  case "$1" in
    --version)
      INPUT_VERSION="${2:-}"
      shift 2
      ;;
    --prerelease)
      PRERELEASE_CHANNEL="${2:-}"
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

if [[ "${PRERELEASE_CHANNEL}" != "stable" && "${PRERELEASE_CHANNEL}" != "alpha" && "${PRERELEASE_CHANNEL}" != "beta" ]]; then
  echo "Invalid prerelease channel: ${PRERELEASE_CHANNEL}. Expected stable, alpha, or beta." >&2
  exit 1
fi

apply_prerelease_channel() {
  local version="$1"

  if [[ "${PRERELEASE_CHANNEL}" == "stable" ]]; then
    echo "${version}"
    return
  fi

  if [[ "${version}" == *"-${PRERELEASE_CHANNEL}" || "${version}" == *".${PRERELEASE_CHANNEL}" ]]; then
    echo "${version}"
    return
  fi

  if [[ "${version}" == *"-"* ]]; then
    local base="${version%%-*}"
    local suffix="${version#*-}"
    echo "${base}-${PRERELEASE_CHANNEL}.${suffix}"
    return
  fi

  echo "${version}-${PRERELEASE_CHANNEL}"
}

if [[ -n "${INPUT_VERSION}" ]]; then
  apply_prerelease_channel "${INPUT_VERSION}"
  exit 0
fi

if [[ ! -f "${BUILD_PROPS_FILE}" ]]; then
  echo "Directory.Build.props not found: ${BUILD_PROPS_FILE}" >&2
  exit 1
fi

BASE_VERSION="$(sed -n 's:.*<PulumiCrossCloudVersion>\(.*\)</PulumiCrossCloudVersion>.*:\1:p' "${BUILD_PROPS_FILE}" | head -n 1)"
if [[ -z "${BASE_VERSION}" ]]; then
  echo "Could not resolve <PulumiCrossCloudVersion> from ${BUILD_PROPS_FILE}" >&2
  exit 1
fi

if [[ "${GITHUB_REF_TYPE:-}" == "tag" && "${GITHUB_REF_NAME:-}" == v* ]]; then
  apply_prerelease_channel "${GITHUB_REF_NAME#v}"
  exit 0
fi

if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
  CI_TIMESTAMP="$(date -u +%Y%m%d%H%M%S)"
  apply_prerelease_channel "${BASE_VERSION}-${CI_TIMESTAMP}"
  exit 0
fi

apply_prerelease_channel "${BASE_VERSION}"
