# 0008. Data ports with demo adapters, proven by contract tests

- **Status:** Accepted
- **Date:** 2026-10-20

## Context

An investigation needs facts that only systems of record hold: which processor a payment went to, a processor's cut-off and time zone, which settlement files arrived, what was paid and from which file, closure days, incidents, error events and risk holds. None of those systems exist here, and the knowledge corpus deliberately leaves these facts out.

The agent's behaviour has to be built and tested against data that is realistic enough to reason over, including records that look related but aren't. The cheapest substitute, faking the agent's tool results in tests, would test the agent against answers written for it, and would leave no evidence that a real data source could be swapped in.

## Decision

**Domain ports with demo adapters over one curated dataset, and contract tests that any implementation must pass.**

- Four ports in `PaymentOps.Domain`: `IMerchantData`, `ISettlementData`, `IProcessorData` and `IErrorEventData`. Each groups what one kind of system would answer; there's no repository per record type.
- `PaymentOps.Data.Demo` implements them over `data/datasets/default/dataset.json`, built into the assembly and checked strictly at startup. Only the web host references it.
- The contract the ports promise is written as tests: half-open ranges with no maximum span, empty results for nothing found, exact ids and stable ordering. The demo adapters run them, and a real adapter's tests would inherit them. An unavailable source throws one exception, `DataSourceUnavailableException`, whatever the source.
- The dataset has a fixed as-of time, so the scenarios reason about the same days whenever they run.
- Fault injection wraps the ports so failure handling can be tested. Only a test host can enable it.

## Consequences

- Tools and the agent are built against the same interfaces a production adapter would implement, and the scenarios run on data with distractors, not on answers written for the test.
- A real adapter is a new implementation plus the inherited contract tests. Paging, freshness and partial failure would be its own concerns.
- The dataset is hand-curated and small. It shows that the scenarios can be solved; it says nothing about scale.
- The ports put no limit on query spans. Limits on what the model may ask belong to the tools that call the ports.

## Alternatives considered

- **Faking tool results in tests.** Faster to write, but it tests the agent against answers written for it, and says nothing about real data sources.
- **A local database.** More realistic queries, but it adds infrastructure and setup to every run for data a single file holds.
- **One dataset per scenario.** Simpler slices, but each scenario would be the only data present, so finding the answer would take no investigation.
