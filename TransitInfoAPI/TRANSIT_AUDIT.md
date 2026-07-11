# TransitInfoAPI — Audit Checklist

## Core Pipelines
- GTFS import
- Shape generation
- Shape carry-forward
- Custom imports
- GTFS-RT polling
- GBFS polling
- Feed validation
- Retry logic

## Logic
- Reconciliation
- Place matching
- Country detection
- Schedule queries
- Dedup logic

## Code Quality
- Dead code
- Stubs
- Magic numbers
- Hardcoding
- Conventions
- Bad practices
- Error handling
- Error responses

## Cross-cutting
- Performance
- Security
- Thread safety
- Cancellation
- Logging
- Configuration
- Caching
- Env config
- CORS policy
- HTTP clients

## Architecture
- EF Core queries
- DI lifetimes
- API contracts
- API versioning
- Pagination
- Frontend admin
- Public map
- Shape editor
- Admin JS
- No tests

## Feed Systems
- Feed polling
- Feed source factory
- Proto parsing
- SqlBulkCopy
- Startup cleanup

## Auth & Ops
- Auth/JWT
- Migrations
- Admin UI pages
- Background workers
- Health checks
- Geo/spatial
- Static files
- Data retention

## PROJECT.md Conventions
- Check if PROJECT.md conventions are followed
