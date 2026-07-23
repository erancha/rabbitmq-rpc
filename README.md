# Durable RPC over RabbitMQ (.NET)

A two-service .NET backend demonstrating durable RPC over RabbitMQ, exercised by a deliberately
minimal Todo domain: a Web API accepts REST calls, delegates every operation through the broker to
a Worker Service — the only PostgreSQL writer — and returns the worker's reply in the HTTP
response.

**Contents:** [Why RabbitMQ RPC?](#why-rabbitmq-rpc) · [Functional Requirements](docs/requirements.md) · [Architecture](#architecture) · [Getting Started](#getting-started) · [Load Testing](docs/load-testing.md)

## Why RabbitMQ RPC?

The REST caller expects the operation's result in the HTTP response, so the services need
request-response semantics — but carrying that traffic over RabbitMQ instead of a direct HTTP call
buys broker-mediated durability and competing-consumer scaling at the cost of extra moving parts:

| Factor                 | Direct HTTP call                                            | RabbitMQ RPC (this project)                                                             | Fire-and-forget messaging                            |
| ---------------------- | ----------------------------------------------------------- | --------------------------------------------------------------------------------------- | ---------------------------------------------------- |
| Response to caller     | Native                                                      | Reply queue + correlation ID                                                             | None — results must be fetched separately            |
| Simplicity             | Simplest: one call, one stack trace                         | Broker, exchange, queues, and correlation IDs to configure and debug                     | Broker plumbing, but no reply path                   |
| Durability             | None — an in-flight request is lost if the callee is down   | Requests persist in durable queues across worker and broker restarts                     | Same durable-queue guarantee                         |
| Horizontal scalability | Requires a load balancer or service discovery               | Add worker replicas; the broker load-balances the shared queue across competing consumers | Same competing-consumer scaling                      |
| Load leveling          | Bursts hit the callee directly                              | The queue absorbs bursts; workers drain at their own pace, bounded by the RPC timeout    | Queue absorbs bursts with no timeout pressure        |
| Temporal coupling      | Both sides must be up simultaneously                        | The worker can restart mid-burst without losing requests; the caller still awaits a reply | None — producer and consumer fully independent       |
| Latency                | Lowest                                                      | Two broker hops per call                                                                 | Not applicable — no reply                            |
| Failure handling       | Errors surface immediately to the caller                    | At-least-once delivery: handlers must be idempotent; failed messages dead-letter for replay | At-least-once, but failures are invisible to the producer |

Choose RabbitMQ RPC when the caller needs the result synchronously **and** durability,
load leveling, or competing-consumer scaling matters. Choose a direct HTTP call when latency and simplicity
dominate and both services are reliably available. Choose fire-and-forget messaging when the
caller does not need a result at all. The trade-offs specific to this codebase (reply queue
design, delivery guarantees) are detailed in
[Architecture → Trade-offs](docs/architecture.md#trade-offs--implementation-notes-what-to-pay-attention-to).

## Architecture

The Web API delegates every request to the Worker Service over RabbitMQ RPC, and the Worker
Service is the only writer to PostgreSQL. The messaging flow, database schema, threading model,
and scalability measurements are described in [docs/architecture.md](docs/architecture.md):

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
