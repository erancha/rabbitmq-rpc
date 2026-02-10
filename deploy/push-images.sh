#!/bin/bash

# Check if username is provided
if [ $# -lt 1 ]; then
    echo "Usage: $0 <docker_hub_username> [tag]"
    exit 1
fi

DOCKER_HUB_USERNAME=$1
TAG=${2:-latest}  # Use second argument if provided, otherwise default to "latest"

# Change to the root directory of the project
cd "$(dirname "$0")/.."

echo -e "\033[36mBuilding images for Docker Hub...\033[0m"

try_command() {
    if ! "$@"; then
        echo -e "\033[31m❌ Error: Command failed: $*\033[0m"
        exit 1
    fi
}

# Build WebAPI image
WEB_API_TAG=$TAG
echo -e "\n\033[33mBuilding WebAPI image...\033[0m"
try_command docker build -t "todo-app:webapi-$WEB_API_TAG" -f src/TodoApp.WebApi/Dockerfile .

# Build Worker image
WORKER_TAG=$TAG
# WORKER_TAG="2-$TAG" # 2 hosted services
echo -e "\n\033[33mBuilding Worker image...\033[0m"
try_command docker build -t "todo-app:worker-$WORKER_TAG" -f src/TodoApp.WorkerService/Dockerfile .

# Test Docker Hub connectivity
echo -e "\033[36mTesting Docker Hub connectivity...\033[0m"
if ! docker pull hello-world:latest > /dev/null 2>&1; then
    echo -e "\033[31mError connecting to Docker Hub. Please ensure you're logged in with 'docker login'\033[0m"
    exit 1
fi

# Tag & push to Docker Hub
echo -e "\033[33mPushing WebAPI image to Docker Hub...\033[0m"
try_command docker tag "todo-app:webapi-$WEB_API_TAG" "$DOCKER_HUB_USERNAME/todo-app:webapi-$WEB_API_TAG"
try_command docker push "$DOCKER_HUB_USERNAME/todo-app:webapi-$WEB_API_TAG"

echo -e "\033[33mPushing Worker image to Docker Hub...\033[0m"
try_command docker tag "todo-app:worker-$WORKER_TAG" "$DOCKER_HUB_USERNAME/todo-app:worker-$WORKER_TAG"
try_command docker push "$DOCKER_HUB_USERNAME/todo-app:worker-$WORKER_TAG"

echo -e "\n\033[32m✅ Successfully built and pushed all images!\033[0m"
echo "You can now run transform-compose.sh to create the deployment configuration."

# Return to original directory
cd "$(dirname "$0")"
