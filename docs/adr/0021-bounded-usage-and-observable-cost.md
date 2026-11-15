# 0021. Bounded usage and observable cost

- **Status:** Accepted
- **Date:** 2026-11-14

## Context

Every investigation is paid for by the call: model tokens for the agent and the screen classifier, Content Safety by the text record. So is every evaluation ([ADR 0020](0020-evaluate-on-request-at-lower-cost.md)). Until now nothing in the system said what an investigation used, and nothing limited how many could run: one at a time per page, twenty questions per conversation, and a budget that only sends email. A demo that stays reachable after its development ends needs both: a measure of what each investigation costs, and limits that hold whoever is signed in.

Cost data in Azure arrives late, up to about a day, so any control driven by the bill acts after the spending it reacts to.

## Decision

**What each investigation uses is measured and traced; how much can run is bounded in the app; a budget-driven cutoff exists as a last resort and stays off while the system is being built.**

- **Usage is measured by what it is.** Requests sent, retries and failures included; Content Safety text records, one per started 1,000 characters of each text, counted only when answered; model tokens per deployment, the screen classifier's included, never counted as Content Safety; and cache hits that sent nothing. A turn whose usage can't be known completely, a response without usage or a request whose outcome is unknown, has no estimated cost: the cost is reported as unavailable, with the reason, and the counts still stand. Prices come from configuration. The estimate uses list prices, without discounts or free allowances, so it's an upper bound.
- **Each investigation is one trace and one decision record.** The trace has spans for the question screen, each tool, each knowledge search, the screening of what tools returned and each model call. The decision record (event `3009`) holds the outcome, the tools and how they ended, the evidence and sections cited, the screen verdicts and the usage. Neither ever holds the question, the answer, tool output or who asked.
- **A pause switch.** While it's on, nothing is investigated and nothing is counted. It's an app setting, so turning the demo off needs no redeploy.
- **Daily limits per person and in total, per UTC day,** counted in a table so a restart doesn't reset them. A reservation reads the person's count and the day's total, refuses without writing if either is at its limit, and otherwise updates both in one transaction conditioned on what it read. Neither limit can be passed, however many reservations run at once, and one count never changes without the other. A conflict reads again, up to five times; storage that fails, or keeps conflicting, refuses: the limits are a cost control, so an unknown count doesn't admit.
- **Only admitted attempts count.** A click while an investigation is running, a paused request and a request over a limit consume nothing. An admitted attempt counts even if the investigation fails or is cancelled, because its screening has already been paid for.
- **The limits live in the web host,** inside the agent's one-at-a-time gate. The evaluation builds the agent's services without them, so it's never paused or capped.
- **Refusals before an investigation starts are counted by reason only:** busy, paused, over the daily limit, usage unavailable. They get no trace and no decision record.
- **A budget-driven cutoff, built but off.** When it's switched on, the budget's 100% notification calls a Logic App that turns off public network access on the one AI account Terraform names, which stops investigations and evaluations alike. It acts only on a notification that matches this budget, subscription and resource group, whose spending has reached its budget; it never takes its target from the notification; it does nothing if access is already off. Re-running the deployment, or one documented command, turns access back on. It stays off while the system is being built, because a late cutoff in the middle of an evaluation would cost more than it saves.
- **The monthly evaluation of `main` runs only while switched on,** with the repository variable `MONTHLY_EVALUATION` set to `on`. It's off by default; a month that's off runs nothing and reports nothing. Manual runs and pull requests are unchanged.

## Consequences

- One investigation can be explained from its investigation id: the decision record, then its trace.
- The demo's running cost is bounded by the daily limits whatever happens, and can be stopped at once with the pause switch.
- Every admitted investigation writes the shared total row, so heavy concurrency retries and, past five conflicts, refuses. At this scale that's rare; the person sees "can't start right now" and can try again.
- The estimate is an upper bound and can't replace the bill; it shows what an investigation uses and how that changes.
- **The cutoff isn't a spending ceiling.** Budget notifications lag the spending by up to about a day, so the cutoff shortens how long overspending continues; it can't stop it from happening.
- The cutoff's identity can update the AI account. Azure's roles can't narrow that to the network setting alone, so it could change other settings of that one account; it has no data-plane role, so it can't call models or read data.
- The Logic App's trigger URL carries a signature, so it's a secret: it lives only in Terraform state and the action group. Anyone who had it could, at worst, switch off access to the one account with a matching notification, which the next deployment undoes. Regenerating the workflow's access keys invalidates it.
- A month with the monthly evaluation switched off goes unevaluated, as `main` changes only through evaluated pull requests.

## Alternatives considered

- **Counting in memory.** Simpler, but the free hosting tier unloads an idle app, which would reset the counts.
- **Two counts updated separately.** Each could be correct alone and still disagree with the other after a failure between them, or let a race pass a limit.
- **No cutoff.** The daily limits bound the app, but not evaluations, which call the account directly; the cutoff is cheap to keep ready.
- **The cutoff on now.** A late cutoff during development would stop evaluations already paid for, until someone restores access.
- **An Entra-secured webhook for the cutoff.** Removes the signed URL, but needs an app registration and its own role setup; the payload checks and the fixed target already limit what the URL can do.
