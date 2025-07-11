# Deployment Instructions

## Table of Contents

<!-- toc -->

- [Prerequisites](#prerequisites)
- [Publishing Images](#publishing-images)
- [Creating Deployment Configuration](#creating-deployment-configuration)
- [Starting the application](#starting-the-application)
  * [Services](#services)

<!-- tocstop -->

This folder contains the deployment configuration for the Todo application with RabbitMQ RPC.

## Prerequisites

- Docker and Docker Compose installed
- PowerShell or bash (for running the scripts)
- Docker Hub account

## Publishing Images

1. Login to Docker Hub:

```powershell
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

## Starting the application

```powershell
docker-compose up -d
```

### Services

The application consists of:

- [WebAPI](http://localhost:5000) (available on localhost:**5000**)
- Worker Service
- RabbitMQ (available on localhost:**5672**) and [Management UI](http://localhost:15672) (available on localhost:**15672**)
- PostgreSQL (available on localhost:**5432**)
