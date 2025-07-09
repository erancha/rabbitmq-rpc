# Deployment Instructions

This folder contains the deployment configuration for the Todo application with RabbitMQ RPC.

## Prerequisites

- Docker and Docker Compose installed
- PowerShell (for running the scripts)
- Docker Hub account

## Publishing Images

1. Login to Docker Hub:
```bash
docker login
```

2. Build and push the images to Docker Hub:
```powershell
.\deploy\push-images.ps1 -DockerHubUsername "your-dockerhub-username"
```

Optionally, you can specify a custom tag:
```powershell
.\deploy\push-images.ps1 -DockerHubUsername "your-dockerhub-username" -Tag "v1.0"
```

## Creating Deployment Configuration

1. Transform the docker-compose.yml using your Docker Hub username:

```powershell
.\transform-compose.ps1 -DockerHubUsername "your-dockerhub-username"
```

2. This will create a `docker-compose.yml` in this folder that uses pre-built Docker images instead of building from source.

3. Start the application:

```bash
docker-compose up -d
```

## Services

The application consists of:
- WebAPI (exposed on port 5000)
- Worker Service
- PostgreSQL (exposed on port 5432)
- RabbitMQ (exposed on ports 5672 and 15672)

## Environment Variables

All necessary environment variables are configured in the docker-compose.yml file.
