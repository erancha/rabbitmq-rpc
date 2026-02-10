# Todo Application

A backend-only (starter) Todo application with separate Web API and Worker services, using RabbitMQ for communication and PostgreSQL for data storage.

## Table of Contents

<!-- toc -->

- [Requirements](#requirements)
  - [Deliverables](#deliverables)
- [Architecture](#architecture)
  - [Features](#features)
  - [Project Structure](#project-structure)
  - [RabbitMQ](#rabbitmq)
    - [Communication Pattern](#communication-pattern)
    - [Error Handling & Reliability](#error-handling--reliability)
    - [Use cases (when RabbitMQ RPC is a good fit)](#use-cases-when-rabbitmq-rpc-is-a-good-fit)
    - [Trade-offs & implementation notes (what to pay attention to)](#trade-offs--implementation-notes-what-to-pay-attention-to)
  - [PostgreSQL](#postgresql)
    - [Schema Design](#schema-design)
    - [Startup](#startup)
  - [Threading Model](#threading-model)
  - [Scalability notes](#scalability-notes)
- [Running the Application](#running-the-application)
  - [Prerequisites](#prerequisites)

<!-- tocstop -->

## Requirements

1. Implement RESTful APIs for managing both User and Item entities.
2. A User can have multiple Items (one-to-many relationship).
3. Design the models using a minimal set of fields necessary for the task.
4. Soft delete should be implemented for deleting an item.
5. Use Entity Framework (EF) with the Code-First approach.
6. Create two separate services:

- Web Service:
  - Exposes the APIs using Swagger.
  - Handles only input validation and verification logic.
- Worker Service:
  - Consumes messages from the web service.
  - Responsible for persisting data to the database.

7. Communication between the services should be handled via RabbitMQ.

### Deliverables

- A Docker Compose file that brings up:
  - The web service
  - The worker service
  - PostgreSQL
  - RabbitMQ
- The Docker Compose setup should allow for running and testing the entire system locally with minimal configuration.
- Code should follow best practices and clean architecture principles as much as possible.

## Architecture

The application implements a robust RabbitMQ RPC communication pattern with direct exchange routing, message persistence, and comprehensive error handling, and uses a clean, minimal database schema with PostgreSQL.

See the diagram for how the WebAPI uses [RabbitMQ RPC Pattern](#rabbitmq-communication-pattern) to delegate requests it receives to the Worker Service and wait for responses.

![Todo App Architecture Diagram](architecture-diagram.svg)

### Features

- RESTful APIs for User and Todo Item management, with Swagger UI for API documentation and testing.
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/) with [Code-First approach](src/TodoApp.WorkerService/Data/TodoDbContext.cs) (defining the schema in C# models/configuration to generate/update the DB via migrations).
- RabbitMQ message-based communication between the services

### Project Structure

- [`src/TodoApp.WebApi`](src/TodoApp.WebApi): Web API service with Swagger UI ([http://localhost:5000/swagger](http://localhost:5000/swagger))
- [`src/TodoApp.WorkerService`](src/TodoApp.WorkerService): Background worker service for data persistence
- [`src/TodoApp.Shared`](src/TodoApp.Shared): Shared models and message contracts

### RabbitMQ

#### Communication Pattern

This application uses RabbitMQ's Direct Exchange with RPC (Remote Procedure Call) for communication between the Web API and Worker services. The flow is:

1.  The WebApi [publishes a messages](src/TodoApp.WebApi/Services/RabbitMQMessageService.cs) (`PublishMessageRpc`) to the exchange, with [a specific routing key](src/TodoApp.Shared/Configuration/RabbitMQ/RoutingKeys.cs) and a unique correlation ID

2.  The Worker Service:

    2.1. [Binds its queues to these routing keys](src/TodoApp.WorkerService/Helpers/RabbitMQSetup.cs) and receives relevant messages.
    - If you start multiple Worker consumers (either multiple `AddHostedService<...>()` registrations in the same process, or multiple Docker `replicas`), they all consume from the same queue(s) as **competing consumers**: each message is delivered to **one** consumer instance (load-balanced by RabbitMQ, optionally influenced by prefetch).

    - RabbitMQ does **not** guarantee exactly-once delivery. With acknowledgements, the practical guarantee is **at-least-once**: if a consumer crashes, disconnects, or fails before acking, RabbitMQ may **redeliver** the message to the same or a different consumer, which can result in **duplicate processing**. To achieve end-to-end “exactly once” behavior, handlers must be idempotent and/or perform deduplication at the application/database level.

    2.2. Processes each request and sends back a response to the request's `reply_to` queue, [including the original correlation ID](src/TodoApp.WorkerService/Services/BaseMessageHandler.cs) (`SendRpcResponse`)

3.  The WebApi:

    3.1. Uses the [correlation ID to locate the pending request](src/TodoApp.WebApi/Services/RabbitMQMessageService.cs) (`consumer.Received +=`) and completes it when a reply is received on the `reply_to` queue

    3.2. [Returns the result](src/TodoApp.WebApi/Controllers/BaseApiController.cs) (`HandleRpcResponse`) to the REST API consumer

**Key Concepts**

- **Exchange**: A direct exchange `todo-app-exchange` routes messages based on simple **routing keys**, since this app needs deterministic 1:1 routing (`user` -> users queue, `todo` -> todos queue) rather than broadcast (fanout), pattern-based topics, or header-based routing
- **Queues**: Two dedicated queues for handling user and todo operations respectively
- **Reply Queues & Correlation IDs**: All RPC requests from one WebApi instance share a single durable reply queue and unique correlation ID to track responses (see **considerations** below)

#### Error Handling & Reliability

- Message persistence ensures durability across broker restarts (WebApi's [Program.cs](src/TodoApp.WebApi/Program.cs) and WorkerService's [Program.cs](src/TodoApp.WorkerService/Program.cs): `durable: true` in exchange and queue declarations)
- Error handling with message acknowledgment ([UserMessageHandler.cs](src/TodoApp.WorkerService/Services/UserMessageHandler.cs) and [TodoItemMessageHandler.cs](src/TodoApp.WorkerService/Services/TodoItemMessageHandler.cs))
- Automatic reconnection with exponential backoff ([Program.cs](src/TodoApp.WebApi/Program.cs) and [Program.cs](src/TodoApp.WorkerService/Program.cs): retry logic with exponential delay)
- Timeout handling for RPC calls ([RabbitMQMessageService.cs](src/TodoApp.WebApi/Services/RabbitMQMessageService.cs): 10-second timeout for RPC responses)

#### Use cases (when RabbitMQ RPC is a good fit)

- Service decoupling with request-response semantics (but without synchronous HTTP coupling)
- Need for durable messaging / broker-mediated delivery
- Offline resilience (worker can keep processing even if the API is briefly unavailable)
- Load distribution across multiple workers

#### Trade-offs & implementation notes (what to pay attention to)

- Higher complexity compared to HTTP communication
- Additional operational overhead for queue management
- Reply queue design optimized for performance and reliability:
  - Each WebApi instance creates one durable, named reply queue at startup ([RabbitMQMessageService.cs](src/TodoApp.WebApi/Services/RabbitMQMessageService.cs))
  - Queue persists across restarts and survives connection failures
  - Correlation IDs route responses to correct requests within the instance
  - Trade-off: Our simpler approach is sufficient for most scenarios, while persistent queues require manual cleanup but may handle higher loads
- Potential debugging complexity in distributed scenarios

### PostgreSQL

#### Schema Design

The application uses a clean, normalized database schema implemented in PostgreSQL.
The schema definitions are managed by the Worker Service and can be found in [Models/](src/TodoApp.Shared/Models/), [Migrations/](src/TodoApp.WorkerService/Migrations/), and [TodoDbContext.cs](src/TodoApp.WorkerService/Data/TodoDbContext.cs)

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

#### Startup

The worker service ensures database availability before processing messages:

1. [DbInitializationService](src/TodoApp.WorkerService/Services/DbInitializationService.cs) runs migrations and verifies database readiness
2. [Message handlers](src/TodoApp.WorkerService/Services/BaseMessageHandler.cs) wait for an [DbInitializationSignal](src/TodoApp.WorkerService/Services/DbInitializationSignal.cs) before consuming messages
3. Once database is ready, the signal is triggered and handlers start processing

### Threading Model

The application uses a multi-threaded architecture with async/await patterns and thread-safety considerations:

**WebAPI Service:**

- **ASP.NET Core Request Threads**: Each HTTP request is implicitly handled on a thread pool thread
- **RPC Reply Consumer**: Single dedicated background thread ([RabbitMQMessageService.cs](src/TodoApp.WebApi/Services/RabbitMQMessageService.cs)) listening on the reply queue
- **Thread-Safe Response Routing**: `ConcurrentDictionary<string, TaskCompletionSource<string>>` maps correlation IDs to pending requests, allowing the single consumer thread to dispatch responses to the correct waiting request thread
- **Channel Pooling**: `ObjectPool<IModel>` provides thread-safe channel reuse for publishing messages

**Worker Service:**

- **Multiple Handler Instances**: 15 `UserMessageHandler` instances and 1 `TodoItemMessageHandler` instance registered as `IHostedService` ([Program.cs](src/TodoApp.WorkerService/Program.cs))
- **Parallel Message Processing**: Each handler runs on its own background thread, consuming messages from RabbitMQ queues independently
- **Per-Message DbContext**: Each message handler creates a new scoped `DbContext` instance per message ([UserMessageHandler.cs](src/TodoApp.WorkerService/Services/UserMessageHandler.cs), [TodoItemMessageHandler.cs](src/TodoApp.WorkerService/Services/TodoItemMessageHandler.cs)) to avoid thread-safety issues with EF Core
- **Initialization Synchronization**: `TaskCompletionSource` with `RunContinuationsAsynchronously` ([DbInitializationSignal.cs](src/TodoApp.WorkerService/Services/DbInitializationSignal.cs)) ensures all message handlers wait for database initialization before processing messages

**Key Thread-Safety Patterns:**

- Async/await throughout for non-blocking I/O operations
- No shared mutable state between message handlers
- Thread-safe collections (`ConcurrentDictionary`) for cross-thread communication
- Object pooling for RabbitMQ channels to prevent concurrent access issues
- Scoped dependency injection for per-request/per-message isolation

### Scalability notes

By scaling the number of message handlers in the worker service from 1 to 15 instances, throughput was increased from ~200 to ~1,000 requests per second (5x improvement). This is achieved by registering multiple instances of the same `IHostedService` class in [Program.cs](src/TodoApp.WorkerService/Program.cs), which creates separate background tasks that process messages in parallel.

The scalability can be tested using the JMeter test plans:

- `jmeter/test-minimal.jmx` (2 threads \* 5 loops)
- `jmeter/test-long.jmx` (200 threads \* 500 loops)

## Running the Application

### Prerequisites

- Docker and Docker Compose
- .NET 8.0 SDK

Run the following script to check dependencies and start the application:

```bash
# Start the application (WSL/Linux)
./start-todo-app.sh

# If you see:
#   /bin/bash^M: bad interpreter: No such file or directory
# it means the script has Windows CRLF line endings.
# Convert it to Unix LF and try again:
dos2unix start-todo-app.sh 2>/dev/null || sed -i 's/\r$//' start-todo-app.sh
chmod +x start-todo-app.sh
./start-todo-app.sh
```

The following services will be available:

- [WebAPI](http://localhost:5000) (available on localhost:**5000**)
- Worker Service
- RabbitMQ (available on localhost:**5672**) and [Management UI](http://localhost:15672) (available on localhost:**15672**)
- PostgreSQL (available on localhost:**5432**, database name: **tododb**)

To stop the application:

```bash
docker compose -p todo-app down

# Optional: also remove volumes (will delete local postgres data)
docker compose -p todo-app down -v
```
