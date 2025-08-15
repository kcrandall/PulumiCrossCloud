#!/bin/bash

# Script to check if Docker image digests match between remote and local
# Usage: ./check-image-digest.sh <image_name> <registry_url>

set -e

IMAGE_NAME="$1"
REGISTRY_URL="$2"
FULL_IMAGE_NAME="${REGISTRY_URL}/${IMAGE_NAME}:latest"

if [ -z "$IMAGE_NAME" ] || [ -z "$REGISTRY_URL" ]; then
    echo "Usage: $0 <image_name> <registry_url>"
    exit 1
fi

echo "Checking digests for: $FULL_IMAGE_NAME"

# Get remote digest
REMOTE_DIGEST=""
if command -v docker &> /dev/null; then
    if docker manifest inspect "$FULL_IMAGE_NAME" &> /dev/null; then
        REMOTE_DIGEST=$(docker manifest inspect "$FULL_IMAGE_NAME" | jq -r '.config.digest // empty' 2>/dev/null || echo "")
    fi
fi

if [ -z "$REMOTE_DIGEST" ]; then
    echo "no-manifest"
    exit 0
fi

# Get local digest
LOCAL_DIGEST=""
if command -v docker &> /dev/null; then
    if docker inspect "$FULL_IMAGE_NAME" &> /dev/null; then
        LOCAL_DIGEST=$(docker inspect "$FULL_IMAGE_NAME" --format='{{index .RepoDigests 0}}' 2>/dev/null | cut -d'@' -f2 || echo "")
    fi
fi

if [ -z "$LOCAL_DIGEST" ]; then
    echo "no-local-image"
    exit 0
fi

# Compare digests
if [ "$REMOTE_DIGEST" = "$LOCAL_DIGEST" ]; then
    echo "match:$REMOTE_DIGEST"
else
    echo "different:remote=$REMOTE_DIGEST,local=$LOCAL_DIGEST"
fi
