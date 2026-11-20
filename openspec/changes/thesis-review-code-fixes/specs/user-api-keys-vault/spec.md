## ADDED Requirements

### Requirement: Persistent Data Protection Key Ring
The Data Protection key ring SHALL be stored in PostgreSQL (`identity."DataProtectionKeys"`) with the application name `AsistenteAyuntamiento`, configured once in `AddInfrastructureServices` so the API, the workers and the evaluation runner share it. It SHALL NOT depend on the container's file system.

#### Scenario: Redeploying the API
- **WHEN** the API container is replaced by a new one
- **THEN** the tenant API keys stored before SHALL still be decrypted

#### Scenario: Worker reads a tenant key
- **WHEN** a worker process decrypts a key the API encrypted
- **THEN** it SHALL use the same key ring and application name and succeed
