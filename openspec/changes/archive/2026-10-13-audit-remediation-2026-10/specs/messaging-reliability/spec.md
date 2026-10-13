## ADDED Requirements

### Requirement: Single Messaging Topology Definition
All RabbitMQ queues, exchanges and arguments SHALL be declared from one definition per runtime: `RabbitMqTopology` in .NET, and a mirrored constant set in Go, documented in `docs/messaging-contract.md`. Each work queue `Q` SHALL have a dead-letter exchange routing to `Q.dlq`, and a retry queue `Q.retry` with a TTL that dead-letters back to `Q`.

#### Scenario: Service startup declares the topology
- **WHEN** any publisher or consumer starts
- **THEN** it SHALL declare `Q`, `Q.retry` and `Q.dlq` with identical arguments
- **AND** no other code path SHALL call `QueueDeclare` with different arguments for the same queue

### Requirement: Durable Publishing
Every message published to a work queue (by the Go scraper, `IngestionAdminService`, or the notification publisher) SHALL be marked persistent and SHALL be confirmed by the broker before the publisher reports success.

#### Scenario: Broker restart with pending messages
- **GIVEN** 100 messages published and not yet consumed
- **WHEN** the RabbitMQ broker restarts
- **THEN** all 100 messages SHALL still be in the queue after the restart

#### Scenario: Publish not confirmed
- **WHEN** the broker does not confirm a publish within the timeout
- **THEN** the publisher SHALL return an error to the caller
- **AND** the caller SHALL NOT mark the document as `Queued`

### Requirement: Bounded Retries and Dead-Lettering
Consumers SHALL retry transient failures up to `MaxRetries` (default 3), using the retry queue and an `x-retry-count` header. Poison messages and messages that exhaust their retries SHALL be routed to `Q.dlq`, never silently dropped.

#### Scenario: Transient failure
- **WHEN** processing a message fails with a transient error (timeout, 5xx from S3 or the LLM)
- **THEN** the message SHALL be republished to `Q.retry` with `x-retry-count` incremented
- **AND** SHALL be redelivered to `Q` after the retry TTL

#### Scenario: Retries exhausted
- **WHEN** a message fails and `x-retry-count >= MaxRetries`
- **THEN** it SHALL be nacked without requeue and delivered to `Q.dlq`
- **AND** the document job state SHALL be set to `Failed` with the error summary

#### Scenario: Malformed message
- **WHEN** a message cannot be deserialized
- **THEN** it SHALL go directly to `Q.dlq` without retries

### Requirement: Resilient Consumer Lifecycle
Consumers SHALL inherit from a single `RabbitMqConsumerBase<TMessage>` that reconnects on connection loss, retries the initial connection indefinitely with backoff, and exposes a health check. A consumer SHALL NOT exit its `ExecuteAsync` while the host is running.

#### Scenario: Broker unavailable at startup
- **GIVEN** RabbitMQ is down when the Worker starts
- **WHEN** RabbitMQ becomes available 5 minutes later
- **THEN** the consumer SHALL connect and start consuming without a restart of the Worker
- **AND** the health endpoint SHALL report Unhealthy until the connection succeeds

#### Scenario: Connection drop while consuming
- **WHEN** the broker closes the connection
- **THEN** the consumer SHALL reconnect and resume consuming
- **AND** unacked messages SHALL be redelivered by the broker

### Requirement: Go Publisher Robustness
The Go scraper publisher SHALL use publisher confirms, reconnect on `NotifyClose`, and serialize channel access across goroutines. Publish errors SHALL be logged and counted in a metric.

#### Scenario: Concurrent publishing
- **WHEN** five provider goroutines publish simultaneously
- **THEN** no channel-level protocol error SHALL occur
- **AND** each publish SHALL either be confirmed or return an error that is logged
