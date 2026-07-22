#!/bin/bash

# Builds and starts the Todo application stack (webapi, worker, postgres, rabbitmq) with Docker
# Compose, then smoke-tests it end to end with deploy/simple-test.sh.
#
#   ./scripts/start.sh [--no-cache] [--follow|-f]
#
# Docker with Compose v2 is the only host prerequisite: both services compile inside the
# mcr.microsoft.com/dotnet/sdk:8.0 build stage. On Windows, run from WSL with Docker Desktop's
# WSL integration enabled for this distro (Docker Desktop: Settings -> Resources -> WSL
# Integration).
#
# Environment:
#   WORKER_REPLICAS   worker replica count (the compose file's default applies when unset)

set -e

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/scripts/docker-compose.yml"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

if command -v docker &> /dev/null; then
    if docker info &> /dev/null; then
        echo -e "${GREEN}✓ Docker is installed: $(docker --version)${NC}"
    else
        echo -e "${RED}Error: Docker is installed but not available in this environment.${NC}"
        echo -e "${YELLOW}If you're running in WSL, enable Docker Desktop WSL integration for this distro (Settings -> Resources -> WSL Integration).${NC}"
        exit 1
    fi
else
    echo -e "${RED}Error: Docker is not installed or not running.${NC}"
    echo -e "${YELLOW}Please install Docker Desktop from https://www.docker.com/products/docker-desktop${NC}"
    exit 1
fi

# Check for Docker Compose (prefer v2: `docker compose`)
if docker compose version &> /dev/null; then
    COMPOSE_CMD=(docker compose)
    echo -e "${GREEN}✓ Docker Compose is installed: $(docker compose version)${NC}"
elif command -v docker-compose &> /dev/null; then
    COMPOSE_CMD=(docker-compose)
    echo -e "${GREEN}✓ Docker Compose is installed: $(docker-compose --version)${NC}"
else
    echo -e "${RED}Error: Docker Compose is not installed.${NC}"
    echo -e "${YELLOW}Install Docker Desktop (recommended) or Docker Compose for your distro.${NC}"
    exit 1
fi

# The compose file lives beside this script rather than at the repo root, so every invocation must
# name it explicitly; relative build contexts inside it resolve against the file's directory.
COMPOSE_CMD+=(-f "$COMPOSE_FILE")

echo -e "\n${CYAN}Starting the Todo application...${NC}"

# docker-helper.sh and the README's compose examples address the stack by this fixed project name.
export COMPOSE_PROJECT_NAME=todo-app

if [ "$1" = "--no-cache" ]; then
    echo "Starting services with no cache..."
    BUILD_FLAGS="--build --no-cache"
else
    echo "Starting services..."
    BUILD_FLAGS="--build"
fi

if ! "${COMPOSE_CMD[@]}" up $BUILD_FLAGS -d; then
    echo -e "${RED}Warning: Issues detected while starting the application.${NC}"
    echo -e "${YELLOW}Checking container status...${NC}"
    "${COMPOSE_CMD[@]}" ps
    echo -e "${YELLOW}You can still use Docker Compose commands to troubleshoot.${NC}"
fi

echo -e "\n${CYAN}Showing container logs...${NC}"
if [ "$2" = "--follow" ] || [ "$2" = "-f" ]; then
    echo "Following logs (Ctrl+C to stop)..."
    "${COMPOSE_CMD[@]}" logs -f
else
    "${COMPOSE_CMD[@]}" ps
    echo -e "\n${GREEN}Services are running in the background.${NC}"
    echo -e "Use './scripts/docker-helper.sh --logs' to follow logs"

    echo -e "${GREEN}Available endpoints:${NC}"
    echo -e "- WebAPI: ${CYAN}http://localhost:5000${NC}"
    echo -e "- RabbitMQ Management UI: ${CYAN}http://localhost:15672${NC} (credentials: guest/guest)"
fi

WAIT_SECONDS=20
echo "Allowing $WAIT_SECONDS seconds for services to be ready..."
sleep $WAIT_SECONDS

GREEN="$GREEN" RED="$RED" YELLOW="$YELLOW" CYAN="$CYAN" NC="$NC" bash "$REPO_ROOT/deploy/simple-test.sh"