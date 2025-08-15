#!/bin/bash

echo "Testing Pulumi deployment fixes..."

# Clean up any existing state
echo "Cleaning up existing state..."
pulumi destroy --yes --stack dev || true

# Clean up temp files
echo "Cleaning up temp files..."
rm -rf /tmp/.pulumi-docker/* || true

# Deploy fresh
echo "Deploying fresh stack..."
pulumi up --yes --stack dev

# Test subsequent deployment
echo "Testing subsequent deployment..."
pulumi up --yes --stack dev

echo "Deployment test completed!"
