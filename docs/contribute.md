# Contribute where users feel the product

Pick one lane. Small, reviewable pull requests land fastest.

## Fastest ways to help

| Lane | Why users care | Start here |
| --- | --- | --- |
| **Kit language packs** | Front-door adoption: “I can steal this today” | `kit/`, patterns + snippets |
| Client examples | Retry-safe callers against the gateway | `samples/` |
| Docs / search clarity | Discovery by failure mode, not jargon | `docs/VISION.md`, FAQ, use-case guides |
| Dashboard UX | Operators resolve uncertainty quickly | `web/admin/`, issue #6 |
| Observability | Alerts and investigation panels | `deploy/grafana/`, Prometheus rules |
| Correctness tests | Trust under concurrency and crash | `tests/` |
| Packaging | Production install paths | `deploy/` Helm, Compose |

## Before you open a PR

1. Reproduce the behavior with Compose or a failing test.
2. Keep the change focused on one concern.
3. Update docs when user-visible behavior changes.
4. Do not weaken duplicate-suppression semantics to make a demo prettier.
5. Never commit secrets, raw payment payloads, or production keys.

## Review preferences

Maintainers prioritize:

- clearer failure semantics;
- safer defaults;
- reproducible tests;
- docs that reduce support questions;
- contributions that unlock a real adopter.

Security issues go through [SECURITY.md](../SECURITY.md), not public issues.

## Discussion prompts that help the roadmap

- “We hit an indeterminate payment after X. Here is the timeline.”
- “Our client stack is Y. This is the helper API we need.”
- “We deploy on Kubernetes and these values must be configurable.”

Open those in [Discussions](https://github.com/Rezakarimzadeh98/retryshield/discussions).
