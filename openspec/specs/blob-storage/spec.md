# Specification: S3-compatible Blob Storage

## Purpose
Store the bulletin documents downloaded by the Go scraper (originals and parsed JSON) in an S3-compatible bucket shared by the scraper and the .NET services: MinIO in development and Cloudflare R2 in production, accessed from .NET only through the `IBlobStorage` port.

## Requirements

### Requirement: S3-compatible Blob Storage Adapter
Blob storage SHALL be accessed through the `IBlobStorage` port, implemented by `S3BlobStorage` over an `AmazonS3Client` (AWSSDK.S3, `ForcePathStyle = true`) in the Infrastructure layer, configured with the `Blob:*` keys (`Blob:Endpoint`, `Blob:AccessKeyId`, `Blob:SecretAccessKey`, `Blob:BucketName`). Locally the AppHost runs a MinIO container; in production the keys point to Cloudflare R2. It is only registered in server-side hosts, never in a browser bundle.

#### Scenario: Local environment
- **WHEN** the AppHost starts without `Blob:Endpoint` in its configuration
- **THEN** it SHALL start MinIO, create the bucket, and pass the MinIO endpoint and generated credentials to the services as `Blob__Endpoint`, `Blob__AccessKeyId` and `Blob__SecretAccessKey`

#### Scenario: Cloudflare R2
- **WHEN** the AppHost configuration sets `Blob:Endpoint`, `Blob:AccessKeyId` and `Blob:SecretAccessKey`
- **THEN** the services SHALL use those values (the R2 S3 endpoint) instead of MinIO
- **AND** the credentials SHALL come from configuration or secrets, never from source code

#### Scenario: Endpoint without credentials
- **WHEN** `Blob:Endpoint` is set but `Blob:AccessKeyId` or `Blob:SecretAccessKey` is missing
- **THEN** startup SHALL fail with an exception saying that `Blob:AccessKeyId` or `Blob:SecretAccessKey` is missing

#### Scenario: Production without blob settings
- **WHEN** the API starts in Production without `Blob:Endpoint`, `Blob:AccessKeyId` or `Blob:SecretAccessKey`
- **THEN** startup SHALL fail listing the missing settings
