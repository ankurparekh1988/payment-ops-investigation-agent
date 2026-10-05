# Architecture (as built)

> Only the project foundation is in place so far: module structure, architecture tests and the web host. Product and AI capabilities come next. This document describes what has actually been implemented, and it's updated in the same pull request as the code it describes.

## Current state

| Area | Status |
|---|---|
| Solution structure | ✅ Projects and allowed dependency directions in place |
| Architecture tests | ✅ Dependency rules enforced in `PaymentOps.ArchitectureTests` |
| Web host | ✅ Placeholder Blazor host with a `/health` endpoint |
| Product and AI capabilities | Not started |

## Project references

The system is a single deployable with modular boundaries. The boundaries are enforced by project references, and architecture tests check them on every build. This diagram shows the actual reference graph from the `.csproj` files.

```mermaid
flowchart TB
  Web["PaymentOps.Web<br/>composition root"]
  Agent["PaymentOps.Agent"]
  Tools["PaymentOps.Tools"]
  Knowledge["PaymentOps.Knowledge"]
  Demo["PaymentOps.Data.Demo"]
  Domain["PaymentOps.Domain<br/>records + data ports"]
  Platform["PaymentOps.Platform"]

  Web --> Agent
  Web --> Tools
  Web --> Knowledge
  Web --> Demo
  Web --> Platform
  Agent --> Tools
  Agent --> Platform
  Tools --> Domain
  Tools --> Knowledge
  Tools --> Platform
  Knowledge --> Domain
  Knowledge --> Platform
  Demo --> Domain
```

*As of 2026-10-05.* `Web` references every module because it's the composition root, where dependency injection wires the system together. `Data.Demo` implements the data ports defined in `Domain`.

| Rule | Enforced by |
|---|---|
| `Domain` has no project references and no Azure, AI or web packages | `DependencyRulesTests` |
| `Tools` never references the demo adapters or the web host | `DependencyRulesTests` |
| Only the composition root (`Web`) references `Data.Demo` | `DependencyRulesTests` |
| Only `Knowledge` uses the Azure AI Search SDK | `DependencyRulesTests` |
| Nothing references the web host | `DependencyRulesTests` |
