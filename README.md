# Todo Application

A backend-only Todo application with separate Web API and Worker services, using RabbitMQ for communication and PostgreSQL for data storage.

## Architecture

See the diagram for how the topic exchange routes messages to Workers' queues based on routing patterns, while temporary reply queues handle RPC responses.

![Todo App Architecture Diagram](architecture-diagram.svg)

This application uses RabbitMQ's Topic Exchange pattern with RPC (Remote Procedure Call) for communication between the Web API and Worker services. Messages are published to a topic exchange (`todos-topic-exchange`) with specific routing keys (e.g., `user.created`, `todo.updated`), and the Worker Service binds its queues to patterns (`user.*` and `todo.*`) to receive relevant messages. Each request includes a temporary reply queue and correlation ID for RPC communication, enabling the Worker to process requests and send responses back to the Web API.

**Why use RabbitMQ RPC?**

- Enables request-response workflows over message queues, simulating synchronous calls while retaining the benefits of message-based decoupling.
- Supports service decoupling, reliability, and load distribution.
- Useful when you need guaranteed delivery, offline resilience, or want to avoid direct HTTP dependencies between services.

**How it works:**

1. The Web API publishes a message (e.g., `CreateUserMessage`) to the topic exchange with a specific routing key (e.g., `user.created`), along with `reply_to` queue and `correlation_id`.
2. The Worker's queue, bound to the topic patterns (`user.*`, `todo.*`), receives matching messages. The Worker processes the request and sends a response to the `reply_to` queue.
3. The Web API waits for and receives the response on its temporary queue, matching it by `correlation_id`.

**Tradeoffs:**

- Introduces blocking/waiting on the client side (Web API) for each request.
- More complex than simple publish/subscribe: requires managing temp queues, correlation IDs, and timeouts.
- Not as easy to trace/debug as HTTP APIs, and not RESTful.

**When to use:**

- When you need request-response over queues, want to decouple services, or require reliable delivery and load distribution.

**When not to use:**

- If you need low-latency, real-time synchronous responses, or simple, observable APIs (consider HTTP/gRPC instead).

## Prerequisites

- Docker and Docker Compose
- .NET 9.0 SDK (for development)

## Running the Application

Run the following script to check dependencies and start the application:

```powershell
.\start-todo-app.ps1
```

The following services will be available:

- Web API: http://localhost:5000/swagger
- RabbitMQ Management: http://localhost:15672 (guest/guest)
- PostgreSQL: localhost:5432

## Project Structure

- `src/TodoApp.WebApi`: Web API service with Swagger UI
- `src/TodoApp.WorkerService`: Background worker service for data persistence
- `src/TodoApp.Shared`: Shared models and message contracts

## Features

- RESTful APIs for User and Todo Item management
- Soft delete implementation for Todo Items
- Entity Framework Core with Code-First approach
- RabbitMQ message-based communication between services
- Swagger UI for API documentation and testing
