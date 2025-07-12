#!/bin/bash

# Log everything to file and console
exec 1> >(tee -a /var/log/ec2-initialization.log) 2>&1

echo "[$(date)] Starting initialization script..."

# Verify required environment variables
if [ -z "$INIT_BUCKET" ]; then
    echo "[$(date)] Error: INIT_BUCKET environment variable is required"
    exit 1
fi

if [ -z "$STACK_NAME" ]; then
    echo "[$(date)] Error: STACK_NAME environment variable is required"
    exit 1
fi

# Create app directory in /opt/$STACK_NAME
mkdir -p "/opt/$STACK_NAME"

# Download docker-compose template from S3
echo "[$(date)] Downloading docker-compose.yml from s3://$INIT_BUCKET/docker-compose.yml"
aws s3 cp "s3://$INIT_BUCKET/docker-compose.yml" "/opt/$STACK_NAME/docker-compose.yml"


# Install Docker
echo "[$(date)] Installing Docker..."
dnf install -y docker.x86_64

# Start Docker service
echo "[$(date)] Starting Docker service..."
systemctl start docker
systemctl enable docker

# Wait for Docker to be ready
echo "[$(date)] Waiting for Docker service to be ready..."
max_attempts=30
attempt=1
while [ $attempt -le $max_attempts ]; do
    if docker info >/dev/null 2>&1; then
        echo "[$(date)] Docker service is ready"
        break
    fi
    echo "[$(date)] Waiting for Docker service (attempt $attempt/$max_attempts)..."
    sleep 2
    ((attempt++))
done

if ! docker info >/dev/null 2>&1; then
    echo "[$(date)] WARNING: Docker service not responding after $max_attempts attempts"
fi

# Install Docker Compose plugin
echo "[$(date)] Installing Docker Compose plugin..."
mkdir -p /usr/local/lib/docker/cli-plugins
curl -SL https://github.com/docker/compose/releases/latest/download/docker-compose-linux-$(uname -m) -o /usr/local/lib/docker/cli-plugins/docker-compose
chmod +x /usr/local/lib/docker/cli-plugins/docker-compose

# Run docker compose
cd /opt/td
docker compose up -d

# Wait for services to fully start
echo "Waiting 10 seconds for services to initialize..."
sleep 10

# Show container status
echo -e "\nContainer Status:"
docker compose ps

# Show system resources
echo -e "\nSystem Resources:"
echo -e "\nCPU and Memory Usage:"
top -b -n 1
echo -e "\nMemory Usage:"
free -h
echo -e "\nDisk Space:"
df -h
echo -e "\nContainer Resource Usage:"
docker stats --no-stream
