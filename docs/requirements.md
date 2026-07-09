# Functional Requirements

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

## Deliverables

- A Docker Compose file that brings up:
  - The web service
  - The worker service
  - PostgreSQL
  - RabbitMQ
- The Docker Compose setup should allow for running and testing the entire system locally with minimal configuration.
- Code should follow best practices and clean architecture principles as much as possible.
