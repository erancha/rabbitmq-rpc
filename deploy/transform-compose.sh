#!/usr/bin/env bash

if [ $# -lt 1 ]; then
    echo "Usage: $0 <docker_hub_username> [tag]"
    exit 1
fi

DOCKER_HUB_USERNAME=$1
TAG=${2:-latest}

cd "$(dirname "$0")"

cp "../scripts/docker-compose.yml" "./docker-compose.yml.tmp"

# Collapse each service's build stanza (service key through its dockerfile: line) into a single
# image: reference, so the generated compose pulls published images instead of building.
sed -i -E '
/[[:space:]]+webapi:/,/[[:space:]]+dockerfile:.*WebApi\/Dockerfile/ c\
  webapi:\
    image: '"$DOCKER_HUB_USERNAME"'/todo-app:webapi-'"$TAG"'
' "./docker-compose.yml.tmp"

sed -i -E '
/[[:space:]]+worker:/,/[[:space:]]+dockerfile:.*WorkerService\/Dockerfile/ c\
  worker:\
    image: '"$DOCKER_HUB_USERNAME"'/todo-app:worker-'"$TAG"'
' "./docker-compose.yml.tmp"

# The source compose pins both services to Development; dropping those lines lets the deployed
# containers fall back to the Production defaults their published images are built for.
sed -i '/ASPNETCORE_ENVIRONMENT/d' ./docker-compose.yml.tmp
sed -i '/DOTNET_ENVIRONMENT/d' ./docker-compose.yml.tmp

mv "./docker-compose.yml.tmp" "./docker-compose.yml"

echo "Created deployment docker-compose.yml with Docker Hub images in the deploy folder"
echo "You can now execute 'docker-compose up' or share $(pwd)/docker-compose.yml"
