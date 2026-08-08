# Contributing

Thank you for helping improve RetryShield. By participating, you agree to the Code of Conduct and license your contribution under the repository license.

If you are choosing where to start, read [`docs/contribute.md`](docs/contribute.md). The short version: pick one user-visible lane, keep the PR small, and protect duplicate-suppression semantics.

## Before starting

Use GitHub Discussions or an issue for substantial behavior, protocol, schema, or security changes. Report vulnerabilities privately as described in `SECURITY.md`. Keep pull requests focused and add an ADR when changing a correctness boundary.

High-value contribution themes:

- client examples and SDKs
- Helm/Kubernetes packaging
- indeterminate reconciliation workflows
- dashboard investigation UX
- adversarial concurrency/crash tests
- docs that map a production incident to a reproducible guide

## Development

Install the .NET SDK version targeted by the projects, Node.js when a web package is present, Docker with Compose, and k6 for load tests.

```sh
dotnet restore RetryShield.slnx
dotnet build RetryShield.slnx --configuration Release --no-restore
dotnet test RetryShield.slnx --configuration Release --no-build
```

For each web package, run `npm ci`, `npm test`, and `npm run build`. Use `docker compose -f deploy/compose.yml config` to validate deployment edits. Prefer `bash scripts/smoke.sh` when changing gateway/demo/Compose behavior.

## Pull requests

- Add tests for behavior and failure paths; do not weaken duplicate-suppression semantics to make a test pass.
- Document user-visible behavior, configuration, migrations, metrics, and operational impact.
- Avoid sensitive data in fixtures, logs, screenshots, and commits.
- Use clear commits and mark breaking changes explicitly.
- Confirm formatting, build, tests, architecture checks, and relevant chaos/load tests.
- Update `CHANGELOG.md` under Unreleased.

Maintainers may request changes, squash or rebase commits, or decline changes that conflict with the guarantees or maintenance capacity. Reviews are technical decisions, not judgments of contributors.
