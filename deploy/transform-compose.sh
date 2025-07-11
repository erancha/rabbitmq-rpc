#!/bin/bash

# Check if username is provided
if [ $# -lt 1 ]; then
    echo "Usage: $0 <docker_hub_username>"
    exit 1
fi

DOCKER_HUB_USERNAME=$1

# Change to the deploy folder
cd "$(dirname "$0")"

# Create a temporary file for transformations
cp "../docker-compose.yml" "./docker-compose.yml.tmp"

# Replace webapi build section with image reference
sed -i -E '
/[[:space:]]+webapi:/,/[[:space:]]+dockerfile:.*WebApi\/Dockerfile/ c\
  webapi:\
    image: '"$DOCKER_HUB_USERNAME"'/todo-app:webapi-latest
' "./docker-compose.yml.tmp"

# Replace worker build section with image reference
sed -i -E '
/[[:space:]]+worker:/,/[[:space:]]+dockerfile:.*WorkerService\/Dockerfile/ c\
  worker:\
    image: '"$DOCKER_HUB_USERNAME"'/todo-app:worker-latest
' "./docker-compose.yml.tmp"

# Move the temporary file to final location
mv "./docker-compose.yml.tmp" "./docker-compose.yml"

echo "Created deployment docker-compose.yml with Docker Hub images in the deploy folder"
echo "You can now execute 'docker-compose up' or share $(pwd)/docker-compose.yml"
