# Durable RPC over RabbitMQ (.NET)

A .NET backend demonstrating durable RPC over RabbitMQ, exercised by a deliberately minimal Todo
domain: a Web API accepts REST calls, delegates every operation through the broker to a Worker
Service — the only PostgreSQL writer — and returns the worker's reply in the HTTP response.

**Contents:** [Functional Requirements](docs/requirements.md) · [Architecture](#architecture) · [Getting Started](#getting-started) · [Load Testing](docs/load-testing.md)

## Architecture

The Web API delegates every request to the Worker Service over RabbitMQ RPC, and the Worker
Service is the only writer to PostgreSQL. Carrying request-response traffic over a broker instead
of a direct HTTP call buys durable queues, load leveling, and competing-consumer scaling at the
cost of extra moving parts; [docs/architecture.md](docs/architecture.md) covers when that
trade-off is worth it, along with the messaging flow, database schema, threading model, and
scalability measurements:

<a href="docs/architecture.md"><img src="docs/architecture-diagram.svg" alt="Todo App Architecture Diagram" width="800"></a>

## Getting Started

### Prerequisites

- Docker and Docker Compose. The services are compiled inside the .NET 8.0 SDK image during the
  Docker build, so no host .NET SDK is required.
- Optional: a host .NET 8 SDK, to build or edit `src/TodoApp.sln` or run the unit tests outside
  Docker (if none is found, `./scripts/run-tests.sh` downloads one into `~/.dotnet`, no sudo
  needed).

To start the application:

```bash
# Start the application (WSL/Linux)
./scripts/start.sh
```

The following services will be available:

- WebAPI service ([http://localhost:5000](http://localhost:5000))
- Worker service(s)
- RabbitMQ (available on localhost:**5672**) and Management UI ([http://localhost:15672](http://localhost:15672))
- PostgreSQL (database name: **tododb**), reachable only from the compose network. To open a shell
  against it: `docker compose -p todo-app exec postgres psql -U postgres -d tododb`

To stop the application:

```bash
./scripts/docker-helper.sh --stop

# Optional: also remove volumes (will delete local postgres data)
./scripts/docker-helper.sh --stop --volumes
```

`docker-helper.sh` can also tail the stack's logs (`--logs`, with severity and service filters) and
show container status (`--ps`); see `./scripts/docker-helper.sh --help`.
