#!/bin/bash

# WSL Setup Instructions:

# 1. System Requirements:
#    - x86_64/AMD64 processor architecture recommended
#    - At least 4GB RAM allocated to WSL
#    - Enable WSL 2 as default: wsl --set-default-version 2
#
# 2. Check WSL version and installation:
#    - Run in PowerShell: wsl --version
#    - If not installed: wsl --install
#    - To upgrade: wsl --update
#
# 3. Recommended Linux Distribution Setup:
#    - Ubuntu is recommended for this app (works well on both Intel/AMD)
#    - Install Ubuntu from Microsoft Store or: wsl --install -d Ubuntu
#    - Check current default and version: wsl -l -v
#    - Set Ubuntu as default WSL environment: wsl --set-default Ubuntu  # This affects WSL terminal integration, not Docker
#    - Verify installation: wsl -l -v
#
# 4. Development Environment Setup:
#    a. Docker Desktop Setup (do this in Windows, not WSL):
#       1. Download and install Docker Desktop from https://www.docker.com/products/docker-desktop
#       2. After installation, enable WSL integration:
#          - Open Docker Desktop
#          - Click on Settings (gear icon)
#          - Go to 'Resources' -> 'WSL Integration'
#          - Check the box next to 'Ubuntu'
#          - Click 'Apply & Restart'
#       3. Verify in WSL by running:
#          docker --version
#          docker compose version
#
#    b. .NET SDK Installation in WSL (required even if you have .NET installed in Windows):
#       - The script runs in WSL environment, so it needs .NET SDK installed in Linux
#       - In WSL Ubuntu terminal, run these commands:
#         # Download and run Microsoft's installation script
#         wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
#         chmod +x dotnet-install.sh
#         ./dotnet-install.sh --version latest
#         rm dotnet-install.sh
#
#         # Add .NET to PATH (two options):
#         # Option 1: Add to current session
#         export DOTNET_ROOT=$HOME/.dotnet
#         export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools
#
#         # Option 2: Add permanently to ~/.bashrc
#         echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
#         echo 'export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools' >> ~/.bashrc
#         source ~/.bashrc  # Apply changes to current session
#
#       - Verify installation: dotnet --version
#
# 5. Navigating and Executing this Script in WSL:
#    First, start WSL by either:
#    - Running 'wsl' in PowerShell, or
#    - Running 'wsl -d Ubuntu --cd "/mnt/c/Projects/dotnet/rabbitmq-rpc"' to start and navigate in one command
#
#    Then:
#    - Your Windows C: drive is mounted in WSL at /mnt/c
#    - To navigate to this script from WSL: cd /mnt/c/Projects/dotnet/rabbitmq-rpc
#    - Make script executable (first time only): chmod +x start-todo-app.sh
#    - Run the script: ./start-todo-app.sh

# 6. Troubleshooting:
#    Docker Desktop Issues:
#    - If docker commands hang or don't respond:
#      1. Close Docker Desktop completely
#      2. Start Docker Desktop and wait for it to fully initialize
#      3. Try running the script again
#
#    WSL Installation Issues:
#    - Run in PowerShell as Administrator:
#      wsl --install -d Ubuntu
#    - If that fails:
#      1. Enable WSL features: dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all
#      2. Restart computer
#      3. Install WSL2 kernel: https://aka.ms/wsl2kernel
#    - Verify with: wsl --list --verbose

# Set error handling
set -e

# Color definitions
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Check for Docker
if command -v docker &> /dev/null; then
    echo -e "${GREEN}✓ Docker is installed: $(docker --version)${NC}"
else
    echo -e "${RED}Error: Docker is not installed or not running.${NC}"
    echo -e "${YELLOW}Please install Docker Desktop from https://www.docker.com/products/docker-desktop${NC}"
    exit 1
fi

# Check for Docker Compose
if command -v docker-compose &> /dev/null; then
    echo -e "${GREEN}✓ Docker Compose is installed: $(docker-compose --version)${NC}"
else
    echo -e "${RED}Error: Docker Compose is not installed.${NC}"
    echo -e "${YELLOW}Docker Compose should be included with Docker Desktop${NC}"
    exit 1
fi

# Check for .NET SDK
if command -v dotnet &> /dev/null; then
    echo -e "${GREEN}✓ .NET SDK is installed: $(dotnet --version)${NC}"
else
    echo -e "${RED}Error: .NET SDK is not installed.${NC}"
    echo -e "${YELLOW}Please install .NET 8.0 SDK from https://dotnet.microsoft.com/download/dotnet/8.0${NC}"
    exit 1
fi

# Start the application
echo -e "\n${CYAN}Starting the Todo application...${NC}"

# Set consistent project name
export COMPOSE_PROJECT_NAME=todo-app

# Check if --no-cache flag is provided
if [ "$1" = "--no-cache" ]; then
    echo "Starting services with no cache..."
    BUILD_FLAGS="--build --no-cache"
else
    echo "Starting services..."
    BUILD_FLAGS="--build"
fi

# Build and start the services
if ! docker-compose up $BUILD_FLAGS -d; then
    echo -e "${RED}Warning: Issues detected while starting the application.${NC}"
    echo -e "${YELLOW}Checking container status...${NC}"
    docker-compose ps
    echo -e "${YELLOW}You can still use 'docker-compose' commands to troubleshoot.${NC}"
fi

# Show containers and endpoints
echo -e "\n${CYAN}Showing container logs...${NC}"
if [ "$2" = "--follow" ] || [ "$2" = "-f" ]; then
    echo "Following logs (Ctrl+C to stop)..."
    docker-compose logs -f
else
    docker-compose logs ps
    echo -e "\n${GREEN}Services are running in the background.${NC}"
    echo -e "Use 'docker-compose logs -f' to follow logs"

    echo -e "${GREEN}Available endpoints:${NC}"
    echo -e "- WebAPI: ${CYAN}http://localhost:5000${NC}"
    echo -e "- RabbitMQ Management UI: ${CYAN}http://localhost:15672${NC} (credentials: guest/guest)"
fi

# Simple test: One user and two todo items
echo -e "\n${CYAN}Running simple test...${NC}"

# Wait for services to be fully ready
echo "Allowing 5 seconds for services to be ready..."
sleep 5

# Create a user
echo "Creating test user..."
MAX_RETRIES=3
RETRY_COUNT=0

while [ $RETRY_COUNT -lt $MAX_RETRIES ]; do
    TIMESTAMP=$(date +%s)
    USER_RESPONSE=$(curl -s -X 'POST' \
      'http://localhost:5000/api/v1/Users' \
      -H 'accept: */*' \
      -H 'Content-Type: application/json' \
      -d "{\"username\": \"testuser_$TIMESTAMP\", \"email\": \"testuser_$TIMESTAMP@gmail.com\"}")
    
    if [[ $USER_RESPONSE == *"createdId"* ]]; then
        # Extract the created user ID
        USER_ID=$(echo $USER_RESPONSE | grep -o '"createdId":[0-9]*' | cut -d ':' -f2)
        echo -e "${GREEN}Created user with ID: $USER_ID${NC}"
        break
    else
        RETRY_COUNT=$((RETRY_COUNT + 1))
        if [ $RETRY_COUNT -lt $MAX_RETRIES ]; then
            echo -e "${YELLOW}Retrying user creation in 2 seconds...${NC}"
            sleep 2
        else
            echo -e "${RED}Failed to create user after $MAX_RETRIES attempts. Response: $USER_RESPONSE${NC}"
            exit 1
        fi
    fi
done

# Create two todo items for the user
echo "Creating test todo items..."
for i in {1..2}; do
    TODO_RESPONSE=$(curl -s -X 'POST' \
      'http://localhost:5000/api/v1/TodoItems' \
      -H 'accept: */*' \
      -H 'Content-Type: application/json' \
      -d "{\"title\": \"Todo $i\", \"description\": \"Description for todo $i\", \"userId\": $USER_ID}")
    
    if [[ $TODO_RESPONSE == *"createdId"* ]]; then
        TODO_ID=$(echo $TODO_RESPONSE | grep -o '"createdId":[0-9]*' | cut -d ':' -f2)
        echo -e "${GREEN}Created todo item $i with ID: $TODO_ID${NC}"
    else
        echo -e "${RED}Failed to create todo item $i. Response: $TODO_RESPONSE${NC}"
    fi
done
