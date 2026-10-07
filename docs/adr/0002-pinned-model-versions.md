# 0002. Pinned model versions, upgraded through evaluation

- **Status:** Accepted
- **Date:** 2026-10-07

## Context

Every model version in Microsoft Foundry is temporary. A generally available version is deprecated about 12 months after release, closing it to new customers, and retired at about 18 months, after which every call returns `410 Gone`.

Each deployment chooses how it reacts to that lifecycle:

- `OnceNewDefaultVersionAvailable`: upgrades automatically when Microsoft names a new default version.
- `OnceCurrentVersionExpired`: stays pinned until retirement, then upgrades automatically.
- `NoAutoUpgrade`: never upgrades, and stops working at retirement.

The chat model's behaviour is what the evaluation suite certifies. The judge's scoring is what makes evaluation results comparable between releases. The embedding model must match the vectors already in the search index: an embedding model that changed underneath the index would degrade retrieval silently, with no errors to notice.

## Decision

Every model deployment (chat, judge and embedding) uses `NoAutoUpgrade` with a pinned version from `ai/manifest.yaml`. A model changes only through a reviewed manifest change that passes the evaluation gate.

Upgrades follow the manifest's slot structure:

- **Chat and judge:** deploy the candidate into the inactive blue/green slot, evaluate it against the current version, switch the active slot, and remove the old one once the new version has settled.
- **Embedding:** build a new index version side by side with the new model, evaluate retrieval, then switch. Vectors from different embedding models can't be mixed.

An upgrade starts when a model enters deprecation (`Deprecating` in the API) or when Microsoft announces its replacement, whichever comes first. Retirement is never the trigger.

## Consequences

- Model behaviour never changes without review and evaluation, and the judge and embedding model stay stable across releases.
- A missed retirement causes an outage instead of an unevaluated upgrade. That risk is managed actively:
  - The plan-time preflight rejects models that are deprecated or retire within the configured buffer, which also stops a recreated environment from deploying them.
  - A scheduled check opens an issue when a model in the manifest approaches deprecation or retirement.
  - Azure Service Health retirement advisories go to the alert email.
- Rejecting deprecated models is stricter than Azure requires: subscriptions that already use a deprecated model may keep deploying it. This deliberately forces the upgrade at about 12 months rather than drifting towards retirement.

## Alternatives considered

- **`OnceCurrentVersionExpired` as a safety net.** Avoids the outage, but an unevaluated model would serve users, and an embedding upgrade would silently break retrieval.
- **`OnceNewDefaultVersionAvailable`.** Always current, but behaviour changes on Microsoft's schedule rather than ours, which defeats the evaluation gate.
