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

This folder contains the deployment configuration for the Todo application with RabbitMQ RPC.

## Prerequisites

- Docker and Docker Compose installed
- Docker Hub account
- bash (for running the scripts)

## Publishing Images

This is **only required** if the images were not already pushed during development (see the root [README](../README.md)).

```bash
# Login to Docker Hub
docker login

# Build and push the images to Docker Hub
./deploy/push-images.sh "your-dockerhub-username"

# Optional: specify a custom tag
./deploy/push-images.sh "your-dockerhub-username" "v1.0"
```

## Creating Deployment Configuration

Transform the root [docker-compose.yml](../docker-compose.yml) using your Docker Hub username:

```bash
./deploy/transform-compose.sh "your-dockerhub-username"
```

This will create or refresh [`deploy/docker-compose.yml`](./docker-compose.yml) that uses pre-built Docker images instead of building from source.

## Starting the application

```bash
docker compose -p todo-app up -d
```

Run the simple smoke test:

```bash
./simple-test.sh
```

To stop the application:

```bash
docker compose -p todo-app down

# Optional: also remove volumes (will delete local postgres data)
docker compose -p todo-app down -v
```

## JMeter load testing

From the repository root you can run the included script.

```bash
# Default (minimal) test
./jmeter/run-test.sh
./jmeter/run-test.sh minimal

# Long test
./jmeter/run-test.sh long
```

### What to expect

These test plans submit `POST /api/v1/Users` requests with a unique `username` + `email` per request (JMeter uses thread/counter/UUID variables), so each successful request should create a new User.

- **Minimal test (`test-minimal.jmx`)**
  - **Load shape**: 2 threads \* 5 loops = **10 requests total**
  - **Expected PostgreSQL effect**: about **10 new rows** in the Users table

- **Long test (`test-long.jmx`)**
  - **Load shape**: 200 threads \* 500 loops = **100,000 requests total**
  - **Expected PostgreSQL effect**: up to **100,000 new rows** in the Users table (assuming all requests succeed)

Notes:

- If some requests fail (timeouts, 5xx, RabbitMQ backpressure, etc.), the number of created rows will be lower.
- Re-running the tests will create additional rows (it is not a fixed-size / idempotent seed), since each request uses a new UUID.

Results are written to:

- `jmeter/results-minimal.jtl`
- `jmeter/results-long.jtl`
