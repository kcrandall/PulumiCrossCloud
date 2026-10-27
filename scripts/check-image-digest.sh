#!/bin/bash

# Script to check if Docker manifest digests match between remote and local.
# Requires Docker with the Buildx plugin.
# Usage: ./check-image-digest.sh <image_name> <registry_url>

set -euo pipefail

if [[ $# -ne 2 || -z "$1" || -z "$2" ]]; then
    echo "Usage: $0 <image_name> <registry_url>"
    exit 1
fi

IMAGE_NAME="$1"
REGISTRY_URL="${2%/}"
FULL_IMAGE_NAME="${REGISTRY_URL}/${IMAGE_NAME}:latest"

echo "Checking digests for: $FULL_IMAGE_NAME"

# Inspect the manifest/index digest, rather than .config.digest (the image config
# is a different blob and cannot be compared with a local RepoDigest).
REMOTE_DIGEST=""
if command -v docker &> /dev/null; then
    REMOTE_DIGEST=$(docker buildx imagetools inspect "$FULL_IMAGE_NAME" --format '{{.Manifest.Digest}}' 2>/dev/null || true)
fi

if [ -z "$REMOTE_DIGEST" ]; then
    echo "no-manifest"
    exit 0
fi

# A local image may have several repository digests. Check all of them instead
# of assuming the first entry belongs to the image reference being checked.
LOCAL_REPO_DIGESTS=$(docker image inspect "$FULL_IMAGE_NAME" --format '{{range .RepoDigests}}{{println .}}{{end}}' 2>/dev/null || true)

if [ -z "$LOCAL_REPO_DIGESTS" ]; then
    echo "no-local-image"
    exit 0
fi

while IFS= read -r REPO_DIGEST; do
    if [[ "${REPO_DIGEST##*@}" == "$REMOTE_DIGEST" ]]; then
        echo "match:$REMOTE_DIGEST"
        exit 0
    fi
done <<< "$LOCAL_REPO_DIGESTS"

LOCAL_DIGEST="${LOCAL_REPO_DIGESTS%%$'\n'*}"
echo "different:remote=$REMOTE_DIGEST,local=${LOCAL_DIGEST##*@}"
