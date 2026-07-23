# Architecture

Every REST call the WebAPI accepts becomes a message: routed through a durable direct exchange to
a durable queue, processed by one of the Worker Service replicas — the only writer to a minimal
PostgreSQL schema — and answered on the calling instance's reply queue matched by correlation ID,
with worker errors returned as typed RPC error responses. The [communication pattern](#communication-pattern)
below walks through this flow step by step.

![Todo App Architecture Diagram](architecture-diagram.svg)

## When is RabbitMQ RPC worth it?

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
load leveling, or competing-consumer scaling matters. Choose a direct HTTP call when latency and
simplicity dominate and both services are reliably available. Choose fire-and-forget messaging
when the caller does not need a result at all. How this codebase implements the reply path and
delivery guarantees is detailed in
[Trade-offs & implementation notes](#trade-offs--implementation-notes-what-to-pay-attention-to).

## Project Structure

- [`src/TodoApp.WebApi`](../src/TodoApp.WebApi): Web API service with Swagger UI ([http://localhost:5000/swagger](http://localhost:5000/swagger))
- [`src/TodoApp.WorkerService`](../src/TodoApp.WorkerService): Background worker service for data persistence
- [`src/TodoApp.Shared`](../src/TodoApp.Shared): Shared models and message contracts

## RabbitMQ

### Communication Pattern

This application uses RabbitMQ's Direct Exchange with RPC (Remote Procedure Call) for communication between the Web API and Worker services. The flow is:

1.  The WebApi [publishes a messages](../src/TodoApp.WebApi/Services/RabbitMQMessageService.cs) (`PublishMessageRpc`) to the exchange, with [a specific routing key](../src/TodoApp.Shared/Configuration/RabbitMQ/RoutingKeys.cs) and a unique correlation ID

2.  The Worker Service:

    2.1. [Binds its queues to these routing keys](../src/TodoApp.WorkerService/Helpers/RabbitMQSetup.cs) and receives relevant messages.
    - The worker runs as multiple Docker `replicas` that all consume from the same queue(s) as **competing consumers**: each message is delivered to **one** replica (load-balanced by RabbitMQ, optionally influenced by prefetch).

    - RabbitMQ does **not** guarantee exactly-once delivery. With acknowledgements, the practical guarantee is **at-least-once**: if a consumer crashes, disconnects, or fails before acking, RabbitMQ may **redeliver** the message to the same or a different consumer, which can result in **duplicate processing**. To achieve end-to-end “exactly once” behavior, handlers must be idempotent and/or perform deduplication at the application/database level.

    2.2. Processes each request and sends back a response to the request's `reply_to` queue, [including the original correlation ID](../src/TodoApp.WorkerService/Services/BaseMessageHandler.cs) (`SendRpcResponse`)

3.  The WebApi:

    3.1. Uses the [correlation ID to locate the pending request](../src/TodoApp.WebApi/Services/RabbitMQMessageService.cs) (`consumer.Received +=`) and completes it when a reply is received on the `reply_to` queue

    3.2. [Returns the result](../src/TodoApp.WebApi/Controllers/BaseApiController.cs) (`HandleRpcResponse`) to the REST API consumer

**Key Concepts**

- **Exchange**: A direct exchange `todo-app-exchange` routes messages based on simple **routing keys**, since this app needs deterministic 1:1 routing (`user` -> users queue, `todo` -> todos queue) rather than broadcast (fanout), pattern-based topics, or header-based routing
- **Queues**: Two dedicated queues for handling user and todo operations respectively
- **Reply Queues & Correlation IDs**: All RPC requests from one WebApi instance share a single exclusive reply queue and unique correlation ID to track responses (see [Trade-offs & implementation notes](#trade-offs--implementation-notes-what-to-pay-attention-to))

### Error Handling & Reliability

- Durable exchange/queue declarations plus persistent request publishing (`properties.Persistent` in [RabbitMQMessageService.cs](../src/TodoApp.WebApi/Services/RabbitMQMessageService.cs)) preserve queued requests across broker restarts
- Requests that fail processing are rejected without requeue and routed through a dead-letter exchange to a durable `dead-letter-queue` ([RabbitMQSetup.cs](../src/TodoApp.WorkerService/Helpers/RabbitMQSetup.cs)) for inspection and replay, instead of being discarded by the broker
- Error handling with message acknowledgment ([BaseMessageHandler.cs](../src/TodoApp.WorkerService/Services/BaseMessageHandler.cs)): each delivery is settled exactly once — acked after successful processing, nacked to the dead-letter queue on failure — before the RPC reply is published
- Connection retries with exponential backoff at startup ([Connections.cs](../src/TodoApp.Shared/Configuration/RabbitMQ/Connections.cs)). OPEN — an established connection or the reply consumer's channel is not automatically recovered if it drops later.
- Timeout handling for RPC calls ([RabbitMQMessageService.cs](../src/TodoApp.WebApi/Services/RabbitMQMessageService.cs): configurable via `WebApi__RpcTimeoutSeconds`, 5 seconds by default)

### Trade-offs & implementation notes (what to pay attention to)

- Reply queue design:
  - Each WebApi instance creates one named, exclusive, auto-delete reply queue at startup ([RabbitMQMessageService.cs](../src/TodoApp.WebApi/Services/RabbitMQMessageService.cs)), instead of a temporary queue per request
  - A single long-lived consumer serves all in-flight requests, avoiding per-request queue/consumer churn
  - Correlation IDs route responses to the correct pending request within the instance
  - The reply queue is deliberately not durable: pending requests live only in the instance's memory, so a reply queue that outlived its instance or a broker restart could never deliver to anyone — the broker removes the queue when the instance's connection closes, so restarts leave nothing behind

## PostgreSQL

### Schema Design

The application uses a clean, normalized database schema implemented in PostgreSQL, defined
Code-First with [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/):
the schema lives in C# models and configuration, and the Worker Service applies it to the database
via migrations. See [Models/](../src/TodoApp.Shared/Models/), [Migrations/](../src/TodoApp.WorkerService/Migrations/), and [TodoDbContext.cs](../src/TodoApp.WorkerService/Data/TodoDbContext.cs)

**Deletion model:** deleting a todo item is a soft delete (`IsDeleted` flag), so an item remains
recoverable while its owner exists. Deleting a user is a hard delete that cascade-removes all of the
user's todo items, soft-deleted ones included — todo history is scoped to its owner's lifetime, and
orphaned history for a nonexistent user has no value.

### Startup

The worker service ensures database availability before processing messages:

1. [DbInitializationService](../src/TodoApp.WorkerService/Services/DbInitializationService.cs) runs migrations and verifies database readiness
2. [Message handlers](../src/TodoApp.WorkerService/Services/BaseMessageHandler.cs) wait for an [DbInitializationSignal](../src/TodoApp.WorkerService/Services/DbInitializationSignal.cs) before consuming messages
3. Once database is ready, the signal is triggered and handlers start processing

## Threading Model

The application uses a multi-threaded architecture with async/await patterns and thread-safety considerations:

**WebAPI Service:**

- **ASP.NET Core Request Threads**: Each HTTP request is implicitly handled on a thread pool thread
- **RPC Reply Consumer**: Single dedicated background thread ([RabbitMQMessageService.cs](../src/TodoApp.WebApi/Services/RabbitMQMessageService.cs)) listening on the reply queue
- **Thread-Safe Response Routing**: `ConcurrentDictionary<string, TaskCompletionSource<string>>` maps correlation IDs to pending requests, allowing the single consumer thread to dispatch responses to the correct waiting request thread
- **Channel Pooling**: `ObjectPool<IModel>` provides thread-safe channel reuse for publishing messages

**Worker Service:**

- **One Handler per Queue**: A single `UserMessageHandler` and a single `TodoItemMessageHandler` registered as `IHostedService` ([Program.cs](../src/TodoApp.WorkerService/Program.cs)), each consuming its queue on a dedicated channel
- **Parallel Message Processing**: Parallelism comes from running multiple worker replicas (see [Scalability notes](#scalability-notes)); within a process, each handler consumes its queue independently
- **Per-Message DbContext**: Each message handler creates a new scoped `DbContext` instance per message ([UserMessageHandler.cs](../src/TodoApp.WorkerService/Services/UserMessageHandler.cs), [TodoItemMessageHandler.cs](../src/TodoApp.WorkerService/Services/TodoItemMessageHandler.cs)) to avoid thread-safety issues with EF Core
- **Initialization Synchronization**: `TaskCompletionSource` with `RunContinuationsAsynchronously` ([DbInitializationSignal.cs](../src/TodoApp.WorkerService/Services/DbInitializationSignal.cs)) ensures all message handlers wait for database initialization before processing messages

**Key Thread-Safety Patterns:**

- Async/await throughout for non-blocking I/O operations
- No shared mutable state between message handlers
- Thread-safe collections (`ConcurrentDictionary`) for cross-thread communication
- Object pooling for RabbitMQ channels to prevent concurrent access issues
- Scoped dependency injection for per-request/per-message isolation

## Scalability notes

The worker scales horizontally: the compose files set `services.worker.deploy.replicas` (the
local compose reads it from the `WORKER_REPLICAS` environment variable), and every replica
consumes the same durable queues as a competing consumer, so RabbitMQ load-balances messages
across replicas. Raising the replica count is the scaling lever; each handler consumes with
`prefetchCount: 1`, so a replica is only handed a message when it is free.
[`scripts/optimize-replicas-count.sh`](../scripts/optimize-replicas-count.sh) searches for the
best count for the host machine.

Each replica runs pending EF migrations at startup. Concurrently starting replicas can race on the
first boot of an empty database; the compose `restart: unless-stopped` policy retries the replica
that loses the race.

Scalability under load can be exercised with the JMeter test plans; see
[Load Testing](load-testing.md).
