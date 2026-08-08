# ADR 0003: Envelope encryption for retained payloads

- Status: Accepted
- Date: 2026-08-08

## Context

Stored request fingerprints and replayable responses may contain sensitive data. Database encryption at rest does not protect against every database-access threat and complicates selective key rotation.

## Decision

Encrypt retained request/response payloads with an authenticated cipher (AES-256-GCM) and per-record nonces. Use envelope encryption: a versioned key-encryption key from an external KMS or secret manager wraps data-encryption keys. Bind record identity, tenant scope, and schema version as authenticated additional data. Store key version and algorithm metadata, never key material, beside ciphertext.

Do not use deterministic encryption for payloads. Compute request fingerprints with a keyed, versioned construction and constant-time comparison. Keys are loaded at runtime, excluded from logs, and rotated by introducing a new write key while retaining read access to prior versions.

## Consequences

Database disclosure alone reveals less content, but availability now depends on key access. Rotation, backup restoration, and disaster recovery must preserve old key versions. Encryption does not replace authorization, TLS, retention limits, or log redaction.
