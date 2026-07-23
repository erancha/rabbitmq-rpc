# Architecture

The WebAPI delegates every request to the Worker Service over RabbitMQ RPC: requests are routed through a durable direct exchange to durable queues, replies come back on a per-instance reply queue matched by correlation ID, and worker errors are returned as typed RPC error responses. The Worker Service is the only writer to a minimal PostgreSQL schema.

See the diagram for how the WebAPI uses the [RabbitMQ RPC pattern](#communication-pattern) to delegate requests it receives to the Worker Service and wait for responses.

![Todo App Architecture Diagram](architecture-diagram.svg)

## Features

- RESTful APIs for User and Todo Item management, with Swagger UI for API documentation and testing.
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/) with [Code-First approach](../src/TodoApp.WorkerService/Data/TodoDbContext.cs) (defining the schema in C# models/configuration to generate/update the DB via migrations).
- RabbitMQ message-based communication between the services

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
- Timeout handling for RPC calls ([RabbitMQMessageService.cs](../src/TodoApp.WebApi/Services/RabbitMQMessageService.cs): configurable via `WebApi__RpcTimeoutSeconds`, 10 seconds by default)

### Use cases (when RabbitMQ RPC is a good fit)

When RabbitMQ RPC is worth its complexity — versus a direct HTTP call or fire-and-forget
messaging — is compared factor by factor in [Why RabbitMQ RPC?](../README.md#why-rabbitmq-rpc).

### Trade-offs & implementation notes (what to pay attention to)

- Reply queue design:
  - Each WebApi instance creates one named, exclusive, auto-delete reply queue at startup ([RabbitMQMessageService.cs](../src/TodoApp.WebApi/Services/RabbitMQMessageService.cs)), instead of a temporary queue per request
  - A single long-lived consumer serves all in-flight requests, avoiding per-request queue/consumer churn
  - Correlation IDs route responses to the correct pending request within the instance
  - The reply queue is deliberately not durable: pending requests live only in the instance's memory, so a reply queue that outlived its instance or a broker restart could never deliver to anyone — the broker removes the queue when the instance's connection closes, so restarts leave nothing behind

## PostgreSQL

### Schema Design

The application uses a clean, normalized database schema implemented in PostgreSQL.
The schema definitions are managed by the Worker Service and can be found in [Models/](../src/TodoApp.Shared/Models/), [Migrations/](../src/TodoApp.WorkerService/Migrations/), and [TodoDbContext.cs](../src/TodoApp.WorkerService/Data/TodoDbContext.cs)

> **Note about Entity Framework Core's Fluent API:**  
> The Fluent API is Entity Framework Core's method for configuring database relationships and constraints using method chaining in C#. For example:
>
> ```csharp
> modelBuilder.Entity<User>(entity => {
>     entity.HasKey(e => e.Id);                    // Sets primary key
>     entity.HasIndex(e => e.Username).IsUnique(); // Sets unique constraint
>     entity.HasMany<TodoItem>()                   // Sets one-to-many relationship
>           .WithOne()
>           .HasForeignKey(e => e.UserId);
> });
> ```
>
> This approach provides more control and flexibility than using attributes/annotations in the model classes.

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
