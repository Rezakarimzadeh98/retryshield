# Changelog

All notable changes are documented here. This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and intends to use [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Product vision: RetryShield as learn + kit + gateway home for safe operations (`docs/VISION.md`).
- Front-door pattern kit under `kit/` (failure story, contract, Node/Express/ASP.NET/FastAPI/webhook snippets).
- Adoption docs: why RetryShield exists, FAQ, contribution map, and launch sharing copy.
- Retry-safe .NET client sample under `samples/dotnet-client`.

### Changed

- README reframed around three entry paths (learn / kit / gateway) while keeping the Compose quick start.
- Contribution map prioritizes kit language packs.
- Sharing copy updated for the broader positioning.

## [0.2.0] - 2026-08-08

### Added

- PostgreSQL Testcontainers coverage for atomic 50-way claims, conflicts, replay persistence, stale recovery, and schema migration history.
- Full-stack Compose smoke test proving one upstream forward and one exact replay.
- Production Compose overlay, configurable upstream URL, and multi-architecture release-image quick start.
- Prometheus alerts, client retry contract, and versioned schema migration guidance.
- Verifiable GitHub build attestations and high-severity image vulnerability gates.

### Changed

- Dashboard API calls now use the same-origin nginx proxy and work outside localhost.
- Container releases now run backend, web, architecture, PostgreSQL, and full-stack checks before publishing.
- Demo services are opt-in and excluded from the production deployment path.

## [0.1.0] - 2026-08-08

### Added

- Durable idempotency state machine with exact response replay and explicit indeterminate outcomes.
- PostgreSQL authoritative storage, optional Redis acceleration, and encrypted stored payloads.
- Safe fixed-upstream gateway, authenticated administration API, and React operations console.
- Production-oriented Compose stack with PostgreSQL, Redis, monitoring, demo upstream, and administration surfaces.
- Multi-stage, non-root container builds.
- CI, security scanning, dependency review, release containers, SBOM, and provenance automation.
- Operational, guarantee, chaos, security, contribution, and architecture documentation.
- k6 concurrency and dependency-failure exercises.

[Unreleased]: https://github.com/Rezakarimzadeh98/retryshield/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/Rezakarimzadeh98/retryshield/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Rezakarimzadeh98/retryshield/releases/tag/v0.1.0
