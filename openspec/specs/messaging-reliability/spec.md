# messaging-reliability Specification

## Purpose
Do not lose ingestion messages: work queues and their retry and dead-letter queues are declared from one shared definition, ingestion messages are persistent and confirmed by the broker, failures are retried a bounded number of times and then dead-lettered, and consumers survive broker outages. Document status notifications are best-effort and outside these guarantees.

## Requirements

### Requirement: Single Messaging Topology Definition
The ingestion queues SHALL be declared from one definition per runtime: `RabbitMqTopology` in .NET and the mirrored constants of `internal/messaging/rabbitmq.go` in Go, documented in `docs/messaging-contract.md`. For each work queue `Q` (`documents_to_process_baseline`, `documents_to_process_hierarchical`) there SHALL be three durable queues: `Q` (dead-letters to `Q.dlq` through the default exchange), `Q.retry` (`x-message-ttl` = 30,000 ms as int32, dead-letters back to `Q`) and `Q.dlq`.

#### Scenario: Service startup declares the topology
- **WHEN** the .NET ingestion publisher, an ingestion consumer or the Go publisher connects
- **THEN** it SHALL declare `Q.dlq`, `Q.retry` and `Q` with identical arguments
- **AND** no other code path SHALL declare those queues with different arguments

### Requirement: Durable Ingestion Publishing
Every ingestion message published by the Go scraper or by `RabbitMqIngestionPublisher` (used by `IngestionAdminService`) SHALL be persistent (`delivery_mode` 2), carry `message_id = document_id` and the W3C trace headers, and be published on a channel with publisher confirms; the publisher SHALL report success only after the broker confirmed it. The Go publisher SHALL wait at most 10 seconds for each confirm; the .NET publisher relies on the client's awaited confirmation and the caller's cancellation token, without its own timeout.

#### Scenario: Broker restart with pending messages
- **GIVEN** 100 confirmed messages not yet consumed
- **WHEN** the RabbitMQ broker restarts
- **THEN** all 100 messages SHALL still be in the queue after the restart

#### Scenario: Publish not confirmed
- **WHEN** a publish fails or is not confirmed
- **THEN** the publisher SHALL return an error to the caller and reset its connection
- **AND** a bulk enqueue SHALL set the document back to `Pending` (it was saved as `Queued` before publishing)

### Requirement: Bounded Retries and Dead-Lettering
Consumers derived from `RabbitMqConsumerBase<TMessage>` SHALL ack a message only after it was handled. When handling fails, they SHALL republish it to `Q.retry` with `x-retry-count` incremented (persistent, confirmed) and ack the original, while `x-retry-count` is below `MaxRetries` (default 3). Messages that exhaust their retries, cannot be deserialized or throw `NonRetryableMessageException` SHALL be nacked without requeue, so the broker routes them to `Q.dlq`.

#### Scenario: Failed message
- **WHEN** handling a message fails for the first time (any exception other than a non-retryable one)
- **THEN** the message SHALL be published to `Q.retry` with `x-retry-count = 1`
- **AND** SHALL be redelivered to `Q` after 30 seconds

#### Scenario: Retries exhausted
- **WHEN** a message fails and its `x-retry-count` is already 3
- **THEN** it SHALL be nacked without requeue and delivered to `Q.dlq`
- **AND** the ingestion consumer SHALL set that pipeline's job status to `Failed` with the error message

#### Scenario: Malformed message
- **WHEN** a message body cannot be deserialized
- **THEN** it SHALL go directly to `Q.dlq` without retries

#### Scenario: Shutdown while handling
- **WHEN** the Worker stops while a message is being handled
- **THEN** the message SHALL be left unacked so the broker redelivers it

### Requirement: Resilient Consumer Lifecycle
Consumers SHALL inherit from `RabbitMqConsumerBase<TMessage>`, which retries the connection indefinitely with exponential backoff (up to 60 seconds), reconnects when the connection closes, reports its state to `RabbitMqConsumerHealth` (health check `rabbitmq-consumers`), and does not leave `ExecuteAsync` while the host runs. The Worker exposes no HTTP endpoint; the health check is only served over HTTP by hosts that map the default endpoints (the API, in Development).

#### Scenario: Broker unavailable at startup
- **GIVEN** RabbitMQ is down when the Worker starts
- **WHEN** RabbitMQ becomes available 5 minutes later
- **THEN** the consumer SHALL connect and start consuming without restarting the Worker
- **AND** `rabbitmq-consumers` SHALL report Unhealthy until the connection succeeds

#### Scenario: Connection drop while consuming
- **WHEN** the broker closes the connection
- **THEN** the consumer SHALL reconnect and resume consuming
- **AND** unacked messages SHALL be redelivered by the broker

### Requirement: Go Publisher Robustness
The Go publisher SHALL use publisher confirms, serialize channel access with a mutex, and, when its channel or connection is closed (`IsClosed`) or a publish fails with `amqp.ErrClosed`, reconnect and retry once. A first connection failure at startup SHALL NOT be fatal. Publish errors SHALL be logged and counted in `scraper.documents.errors` with `stage = publish`.

#### Scenario: Concurrent publishing
- **WHEN** several document goroutines publish simultaneously
- **THEN** no channel-level protocol error SHALL occur
- **AND** each publish SHALL either be confirmed on both queues or return an error that is logged and counted

#### Scenario: Connection lost between documents
- **WHEN** the AMQP connection was closed since the last publish
- **THEN** the next publish SHALL reconnect, redeclare the topology and publish

### Requirement: Best-Effort Status Notifications
Document status notifications SHALL NOT use the work-queue guarantees. The Worker's `RabbitMqNotificationPublisher` SHALL publish `{ DocumentId, NewStatus }` to the durable fanout exchange `document_notifications_exchange` on a channel without publisher confirms, and log and swallow any error. Each API instance SHALL consume them through its own exclusive, auto-delete queue `api_notifications_{guid}` and drop failed messages without retry or dead-lettering.

#### Scenario: API instance down
- **WHEN** a Worker publishes a notification while no API instance is connected
- **THEN** the notification SHALL be lost and the ingestion SHALL continue unaffected
