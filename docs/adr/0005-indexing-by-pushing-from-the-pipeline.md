# 0005. Indexing by pushing from the pipeline

- **Status:** Accepted
- **Date:** 2026-10-16

## Context

The knowledge corpus has to reach Azure AI Search with key authentication disabled, and the index has to hold exactly the current sections: a section that was renamed or removed must not stay retrievable, and a document that moves to `restricted` must never be readable as `general`, even briefly.

Search can pull content itself: an indexer reads Blob storage and, with Markdown parsing in one-to-many mode, creates one document per heading. That needs a data source and an indexer, and a managed identity on the search service to read storage without keys, which the Free tier doesn't offer. It also doesn't solve the hard part: re-indexing a changed file in one-to-many mode can leave stale documents behind when a section is removed, so we'd still need our own clean-up, outside the indexer's control.

## Decision

**CD pushes the sections with a small publisher, `tools/PaymentOps.KnowledgePublish`, after building the corpus.**

- Sections are split with the same Markdown parse that validated the corpus, one search document per heading with text of its own.
- Each section's key is the doc id plus the SHA-256 of its length-prefixed heading path.
- The publisher reads every stored document and compares every field with the corpus, rather than trusting content hashes, so a classification change or an edit made outside the pipeline is seen.
- Sections of a document that has become restricted are deleted and confirmed gone by key lookup before anything is uploaded. New and changed sections are uploaded next, and stale ones deleted last. The run ends by reading every document back and requiring an exact match.
- The index name comes from `retrieval.index` in the AI release manifest. A schema change Search can't make in place gets a new name.
- The pipeline signs in with its own identity, which holds Search Service Contributor and Search Index Data Contributor. Search has no identity of its own.

## Consequences

- We own about 200 lines of reconciliation code. It's covered by deterministic tests against an in-memory index, and by a self-test (`knowledge-selftest.yml`, run by hand) that exercises the same behaviours against a throwaway index on the real service.
- There's no indexer, data source, skillset or search-side identity to configure, secure or pay for.
- Publishing runs on every deployment, so a free service that Microsoft deletes after inactivity is refilled by the next one. When nothing changed, no section is uploaded or deleted; the index definition is still re-applied, which doesn't change anything.
- A section is briefly missing while it's tightened. That's the intended trade: missing is safe, wrongly visible isn't.
- A document with no section text is refused at corpus build, because nothing in the index would carry its classification.
- Once the app reads the index, a new index generation has to be built and verified before an app that uses it goes live. That ordering arrives with retrieval.

## Alternatives considered

- **Indexer over Blob storage with Markdown parsing.** Less code of our own, but it needs a search-side managed identity (not on the Free tier), and its one-to-many mode can leave stale sections, which this design exists to prevent.
- **Comparing content hashes only.** Cheaper to compare, but moving a file between folders changes its classification and not its hash, so the most security-relevant change would be missed.
- **Rebuilding the index on every deployment.** Simple and always exact, but the index would be empty or partial during every deployment, and once the app reads it, every deployment would interrupt retrieval.
