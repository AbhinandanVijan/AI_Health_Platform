# Documentation Index

Start here for project and operations documentation.

## Core Documents

- `../README.md`
  - Repository entrypoint, architecture overview, quick start.

- `PROJECT_DOCUMENTATION.md`
  - Canonical system and product architecture.
  - Runtime components, API surface, domain model, configuration, and operational notes.

- `AWS_FREE_TIER_DEPLOYMENT.md`
  - AWS-specific deployment and runbook.
  - EC2/RDS/S3/SQS setup, compose commands, validation, and troubleshooting.

- `PROBLEMS_AND_RESOLUTIONS.md`
  - Chronological incident log with root causes and fixes.

## Suggested Reading Paths

### New developer

1. `../README.md`
2. `PROJECT_DOCUMENTATION.md`
3. `PROBLEMS_AND_RESOLUTIONS.md`

### Deploy or operate on AWS

1. `AWS_FREE_TIER_DEPLOYMENT.md`
2. `PROJECT_DOCUMENTATION.md` (runtime and config context)
3. `PROBLEMS_AND_RESOLUTIONS.md` (known failure patterns)

### Debug production issue

1. `PROBLEMS_AND_RESOLUTIONS.md`
2. `AWS_FREE_TIER_DEPLOYMENT.md` (troubleshooting playbook)
3. `PROJECT_DOCUMENTATION.md` (expected behavior and architecture)

## Scope and Ownership

- Keep architecture and API behavior updates in `PROJECT_DOCUMENTATION.md`.
- Keep cloud/environment runbook updates in `AWS_FREE_TIER_DEPLOYMENT.md`.
- Record every meaningful incident/fix in `PROBLEMS_AND_RESOLUTIONS.md`.

## Update Checklist

When architecture or deployment changes:

1. Update affected sections in `PROJECT_DOCUMENTATION.md`.
2. Update corresponding commands/notes in `AWS_FREE_TIER_DEPLOYMENT.md`.
3. Add incident entry in `PROBLEMS_AND_RESOLUTIONS.md` if the change came from a failure or hotfix.
4. Ensure this index still points to the right primary docs.
