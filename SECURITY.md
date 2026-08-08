# Security policy

## Supported versions

Security fixes are provided for the latest released minor version. Until the first stable release, only the latest commit on the default branch is supported. Pin production deployments to a reviewed release digest.

## Reporting a vulnerability

Do not open a public issue. Use GitHub's **Security → Report a vulnerability** private reporting flow. If private reporting is unavailable, contact the maintainers through a private channel listed on the repository owner profile and request a secure reporting address; do not include exploit details in the first message.

Include the affected version/commit, deployment assumptions, reproduction steps, impact, and any suggested mitigation. Remove customer data, credentials, and live idempotency keys. We aim to acknowledge reports within 3 business days, provide an initial assessment within 7 business days, and coordinate disclosure after a fix is available. These are targets, not service-level guarantees.

## Security boundaries

RetryShield must be deployed behind TLS and authenticated ingress. The admin API, dashboard, database, Redis, Prometheus, and Grafana are private management surfaces. PostgreSQL is the correctness authority; Redis is not. Operators own secret management, tenant isolation, backups, restore testing, retention, upstream authorization, and network policy.

Idempotency keys are bearer-like correlation values, not authentication. Do not log raw keys or payloads. An indeterminate result means an upstream side effect may have occurred and requires reconciliation.

## Hardening checklist

- Generate independent high-entropy secrets and store them in a secret manager.
- Use a managed KMS and preserve key versions needed by backups.
- Restrict egress to configured upstreams and ingress to trusted proxies.
- Run containers as non-root with read-only filesystems and pinned image digests where practical.
- Apply database least privilege, TLS, patching, audit logging, and tested PITR.
- Protect metrics from high-cardinality identifiers and sensitive labels.
- Review dependency, CodeQL, Scorecard, SBOM, and provenance results before release.
