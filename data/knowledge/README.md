# Knowledge sources

The operational documents the assistant can search: runbooks, processor operating guides, policies, escalation procedures and postmortems. Each file is reviewed Markdown; `tools/PaymentOps.CorpusBuild` validates them and writes what gets indexed.

## What belongs here

Facts that a system of record already holds come from tools, not from these documents. That includes payment and settlement status, routing, processor responses, configured cut-offs and other processor settings, outage events, closure calendars, risk holds, risk rule ids and their thresholds.

A document here explains how to interpret those facts or what to do about them. Before adding one, ask whether it would exist as a real operational document, or whether it is configuration written as prose. Configuration goes into the demo data instead.

| Belongs here | Doesn't |
|---|---|
| "Compare the outage window with the processor's configured cut-off." | "Fabrikam Pay's cut-off is 16:00 ET." |
| "A partial file is followed by a second file with the same batch date." | A list of settlement files received last week |
| "RF-37 flags a volume spike; first ask whether one was expected." | "RF-37 triggers at five times the 90-day average." |

## Layout

```text
src/
  general/      readable by every signed-in user
  restricted/   readable by Risk & Compliance only
```

Classification comes from the folder, never from the file. Moving a file between the folders changes who can read it and nothing else. The repository is public, so "restricted" decides what the assistant shows to whom, not who can read the source.

## File format

Each file starts with front matter, followed by the body:

```markdown
---
docId: missing-settlement-batch
title: Missing settlement batch
docType: runbook
---

# Missing settlement batch
```

- `docId` is the file name without `.md`: lowercase words joined by hyphens, at most 64 characters. It is the document's permanent identity, so don't reuse or rename one.
- `docType` is one of `runbook`, `processor-doc`, `policy` or `incident-report`.
- The front matter is build metadata and isn't indexed; only the body is.

## Headings and citations

Search splits each document by heading, and a citation points at a doc id and a heading path such as *Risk hold review policy > Risk rules > RF-37 volume spike*.

- The body starts with one `#` heading that matches the title, and it's the only one.
- Use `##` and `###` below it without skipping a level. Nothing deeper.
- Write headings with `#`, as plain text, and keep each heading path unique within the document.
- A heading path is stable only while the headings are. Renaming a heading changes it, which is fine.

## Public repository hygiene

No HTML or images. Links are absolute `https` URLs. Email addresses and URLs use reserved example domains (`example.com`, `.example`), and nothing may look like a card number. The build checks all of these except bare host names written without `https://`, which review has to catch.

## Building

```bash
dotnet run --project tools/PaymentOps.CorpusBuild -- data/knowledge/src --out artifacts/knowledge
```

The output holds each body as `<docId>.md` and `corpus.json`, which records every document's metadata, heading paths and content hash.
