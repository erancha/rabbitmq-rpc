# Deployment Instructions

## Table of Contents

<!-- toc -->

- [Prerequisites](#prerequisites)
- [Publishing Images](#publishing-images)
- [Creating Deployment Configuration](#creating-deployment-configuration)
- [Starting the application](#starting-the-application)
- [JMeter load testing](#jmeter-load-testing)
  * [What to expect](#what-to-expect)

<!-- tocstop -->

This folder contains the tooling to publish the Todo application's Docker images and to generate a deployment configuration that runs from those published images instead of building from source.

All commands below are meant to be run from the repository root.

## Prerequisites

- Docker and Docker Compose installed
- Docker Hub account
- bash (for running the scripts)

## Publishing Images

This is **only required** if the images are not already available on Docker Hub.

```bash
# Login to Docker Hub
docker login

# Build and push the images to Docker Hub
./deploy/push-images.sh "your-dockerhub-username"

# Optional: specify a custom tag
./deploy/push-images.sh "your-dockerhub-username" "v1.0"
```

## Creating Deployment Configuration

Transform the source [docker-compose.yml](../scripts/docker-compose.yml) using your Docker Hub username:

```bash
./deploy/transform-compose.sh "your-dockerhub-username"
```

This will create or refresh `deploy/docker-compose.yml` (git-ignored, generated locally) that uses pre-built Docker images instead of building from source.

## Starting the application

```bash
docker compose -f deploy/docker-compose.yml -p todo-app up -d
```

Run the simple smoke test:

```bash
./deploy/simple-test.sh
```

To stop the application:

```bash
docker compose -f deploy/docker-compose.yml -p todo-app down

# Optional: also remove volumes (will delete local postgres data)
docker compose -f deploy/docker-compose.yml -p todo-app down -v
```

## JMeter load testing

With the application running, drive load against it using the included helper.

```bash
# Default (minimal) test
./scripts/jmeter-helper.sh

# Long test
./scripts/jmeter-helper.sh --long
```

### What to expect

These test plans submit `POST /api/v1/Users` requests with a unique `username` + `email` per request (JMeter uses thread/counter/UUID variables), so each successful request should create a new User.

- **Minimal test (`test-minimal.jmx`)**
  - **Load shape**: 2 threads \* 5 loops = **10 requests total**
  - **Expected PostgreSQL effect**: about **10 new rows** in the Users table

- **Long test (`test-long.jmx`)**
  - **Load shape**: 200 threads \* 250 loops = **50,000 requests total**
  - **Expected PostgreSQL effect**: up to **50,000 new rows** in the Users table (assuming all requests succeed)

Notes:

- If some requests fail (timeouts, 5xx, RabbitMQ backpressure, etc.), the number of created rows will be lower.
- Re-running the tests will create additional rows (it is not a fixed-size / idempotent seed), since each request uses a new UUID.

Passing `--jtl` to the helper writes per-request results to:

- `jmeter/results-minimal.jtl`
- `jmeter/results-long.jtl`
