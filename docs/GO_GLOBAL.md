# RetryShield go-global plan

This plan scales RetryShield from strong engineering to broad open-source adoption.

## Phase 1 - credibility baseline (week 1)

- Keep quick-start path copy-paste reliable on Linux and Windows.
- Keep CI, CodeQL, and container workflows green.
- Keep guarantees documentation visible and versioned.
- Ensure issue templates route bug reports with reproduction data.

Success metrics:
- First successful quick-start for new user in < 10 minutes
- No stale release badge or broken docs links

## Phase 2 - contributor velocity (weeks 2-4)

- Create contributor tracks: docs, gateway, admin UI, packaging.
- Label and scope at least 12 actionable issues.
- Add one architecture-focused contribution guide linked to ADRs.
- Maintain rapid maintainer review cadence.

Success metrics:
- First review turnaround < 48h
- At least 5 external PRs merged per month

## Phase 3 - ecosystem expansion (month 2)

- Publish client examples for more stacks (Node, Python, Go).
- Add migration notes from ad-hoc idempotency middleware.
- Add public reliability incident write-ups as learning material.
- Add benchmark snapshots for concurrency and replay correctness.

Success metrics:
- More downstream sample usage in issues/discussions
- Rising GHCR pulls and repeat doc visits

## Phase 4 - global scale operations (ongoing)

- Publish monthly release notes with reliability outcomes.
- Keep one public roadmap lane for community-requested features.
- Run periodic office-hour thread in Discussions.
- Keep security disclosure and patch process predictable.

Success metrics:
- Growth in stars/forks over rolling 8-week windows
- External contributor retention > 25%

## Operating rules

- Preserve protocol guarantees first; UI polish second.
- Never market "exactly once" as a promise.
- Keep production guidance conservative and explicit.
- Prefer measurable reliability improvements over feature volume.
