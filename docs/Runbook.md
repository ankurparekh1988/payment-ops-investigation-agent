# Runbook

What to do when the agent may have been steered or may have shown something it shouldn't. Logs hold ids, counts and timings, never content, so the investigation id on the result's footer is how everything is traced.

**Finding an investigation's logs.** In the environment's Log Analytics workspace:

```kusto
AppTraces
| where Message has "<investigation id>"
| project TimeGenerated, SeverityLevel, Message
| order by TimeGenerated asc
```

The relevant events: `3001` assessed, `3002` not assessed, `3005` untrusted values screened and withheld (counts per tool), `3006` blocked by the content filter (prompt or completion side), `3007` a question not investigated by the question screen (harmful, self-harm, attack or unavailable), `3008` a merchant the user never named refused, `3009` the decision record, `3101` an approval decision, `3301` a pause or resume.

**Explaining an answer.** The decision record (`3009`) says what the investigation did: its outcome, the tools it called and how each ended, the evidence and sections it cited, the screen verdicts, and what it used. Its operation id finds the rest of the trace, the screens, searches, tool calls and model calls in order:

```kusto
let investigation = "<investigation id>";
let operation = AppTraces
    | where Properties.InvestigationId == investigation and Message has "decision:"
    | project OperationId;
union AppTraces, AppDependencies
| where OperationId in (operation)
| project TimeGenerated, Type, Name, DurationMs, Message, Properties
| order by TimeGenerated asc
```

## Suspected prompt injection

Signs: a result with a gap saying text was withheld because it looked like an instruction; an assessment or proposed ticket that doesn't follow from its evidence; a proposal for a processor or merchants the question wasn't about.

1. **Don't approve.** Reject the proposal on its card. A pending proposal also expires after 15 minutes.
2. **Find the source.** Note the investigation id. Event `3005` names the tool whose text was withheld; the result's gaps name the source. For knowledge, open the cited section's source page, which shows the original text to an authorized person. For data, look up the merchant, processor or incident in its system of record.
3. **If the screen flagged it,** the control worked: the model never read the text. Correct or remove the source. Knowledge changes go through a pull request to `data/knowledge/` and the evaluation gate; data belongs to its owning system.
4. **If the screen didn't flag it** and the result was steered, treat it as a miss. Record the text and the investigation id, add a case with that text to the evaluation, and raise it with the maintainer before the next behaviour change merges.
5. **If many values are unscreenable** (event `3005` with unscreenable counts, gaps saying text couldn't be checked), Prompt Shields or the harm analysis is failing or throttled. Investigations continue on structured data with text withheld; check the Foundry account's health and the app identity's *Prompt Shields User* assignment.
6. **If questions aren't being investigated at all** (event `3007` with `Unavailable`; people see "couldn't be checked for safety"), a question check is failing. Check the same assignment, that the role holds the harm analysis action, which only the bootstrap adds, and the `screen` deployment, which the classifier calls.

## Suspected disclosure

Signs: a General user sees Restricted knowledge; a ticket's title or summary carries bank details or personal data; an answer quotes something the asker shouldn't have.

1. **Stop the spread.** Reject a pending proposal. A created ticket goes to whoever owns the ticket system to be withdrawn.
2. **Confirm who saw what.** From the investigation id, find the signed-in user and their roles in Entra; check the section's classification in the corpus and the user's membership of the Restricted group.
3. **Find the cause.** A misclassified section is fixed in the corpus by pull request. A wrong group membership or app role is fixed in Entra. Text that passed the ticket content policy means a rule is missing; add it with a test.
4. **Record it.** Note the investigation id, what was seen and by whom, the cause and the fix. The data here is synthetic, so no notification duty applies; with real data, follow the organisation's incident process.

## Pausing and limits

Investigations can be stopped without a deployment, and how many can run in a day is bounded ([ADR 0021](adr/0021-bounded-usage-and-observable-cost.md)).

- **Pause from the app.** An Admin uses **Pause investigations** in the Operations section of the home page, with an optional reason. New investigations get "Investigations are paused." within 10 seconds on every instance; running ones finish. **Resume investigations** undoes it. The section shows who last changed it, when and why, and each change is logged as event `3301` with a pseudonymous id and the reason's length; the workbook's Operations section lists them.
- **Pause by configuration.** Set the web app's `PaymentOps__Usage__Paused` setting to `true`; the app restarts and every question gets "Investigations are paused." Nothing is investigated or counted, whatever the switch says, and the home page shows "Paused by configuration". Set it back to `false`, or remove it, to resume. Terraform's `investigations_paused` variable sets the same value at the next deployment, so change it there too or the deployment undoes the pause.
- **Daily limits.** `PaymentOps__Usage__DailyLimitPerUser` and `PaymentOps__Usage__DailyLimitTotal` bound how many investigations one person and everyone together may start in a UTC day. Counts reset at 00:00 UTC. Only admitted investigations count; a refused one consumes nothing.
- **"Investigations can't start right now."** The usage table, or the pause row in it, couldn't be read or written, so nothing was admitted (event `3201` names the cause's type). Check the web app identity's *Storage Table Data Contributor* assignment on the `usage` table (not the account) and the storage account's health.
- **Who's refused, and why.** Refusals before an investigation starts are counted by reason in the `paymentops.investigations.rejected` metric (`busy`, `paused`, `daily_limit`, `usage_unavailable`); they have no trace.

## Alerts

Every alert emails the environment's action group. The workbook *`<prefix>-<environment>` operations* in the resource group shows the same signals over the last week: requests, server errors, latency, throttling and availability; outcomes, screen blocks and refusals; tokens, Content Safety records and estimated cost.

**Server errors.** The web app answered with HTTP 5xx more than the threshold in 15 minutes. Check the app's log stream and `AppExceptions`; a failed deployment or a missing setting fails every request, and startup names the missing setting. 4xx responses don't count: they're the caller's to fix.

**Model throttling.** Requests to the AI account, the model's endpoint or Content Safety's, were refused with 429 in the last 15 minutes. Investigations retry once and then report that the service isn't responding; questions may go unscreened. Check the deployments' capacity against `ai/manifest.yaml`, and whether an evaluation was running at the time: it shares the account.

**Availability.** The liveness endpoint `/health` failed its availability test, which runs every 15 minutes. The app isn't answering at all: check the web app's state and log stream, and whether a deployment was running. `/health` touches no paid service, so a failure here is the app or its plan, not the model.

**Guardrail block spike.** More questions blocked, or tool text withheld, than the threshold in 15 minutes. Find the investigations in the decision records (`3009` with `screens` other than clean) and treat them as [suspected prompt injection](#suspected-prompt-injection) if the blocks are on tool text. A burst of blocked questions from one person is worth a word with them; the daily limits bound it either way.

**Firing each alert once.** After a deployment that changes an alert, fire it to confirm it reaches the inbox: for server errors, set a temporary threshold of `0` in `TF_VAR_http5xx_threshold` and request a page that errors; for guardrail blocks, ask a question the screen blocks after a threshold of `0`; for throttling and availability, use the alert rule's *Test action group* in the portal. Restore the thresholds afterwards.

## Budget cutoff

A Logic App that turns public network access off on the AI account when the budget's 100% notification arrives, stopping investigations and evaluations alike ([ADR 0021](adr/0021-bounded-usage-and-observable-cost.md)). **It isn't a spending ceiling:** budget notifications lag spending by up to about a day, so it shortens overspending rather than preventing it.

- **Off by default.** With `TF_VAR_budget_cutoff_enabled` unset or `false`, the workflow is disabled and the budget doesn't call it. Deployment never switches it on. Set it to `true` and deploy to switch it on.
- **What it acts on.** Only this budget's own notification, from this subscription and resource group, with spending at or past the budget; anything else ends as *Skipped: unexpected notification*. It never takes its target from the notification, and does nothing if access is already off.
- **When it has fired.** Investigations report that the service isn't responding and evaluations can't reach the model. Its runs, with their status, are in the workflow's run history and in Log Analytics (`AzureDiagnostics` for the workflow, category `WorkflowRuntime`); the account update appears in the Activity Log under its identity.
- **Restoring access.** Re-run the deployment, which states network access as enabled, or:

  ```bash
  az resource update --ids <AI account resource id> --set properties.publicNetworkAccess=Enabled
  ```

  The 100% notification fires once per budget period, so a restore isn't undone that month.
- **Testing it.** With the owner's approval: switch it on, send the action group's budget test notification, confirm the run's status and that access is off, then restore access and switch it back off. This also confirms the live notification carries the fields the workflow checks.
- **Its trigger URL is a secret.** It carries a signature, so anyone with it and a matching payload could turn access off; the next deployment restores it. To invalidate it, regenerate the workflow's access keys (`az rest --method post --url "<workflow id>/regenerateAccessKey?api-version=2019-05-01" --body '{"keyType":"Primary"}'`) and re-run the deployment, which reads the new URL into the action group.

## End of the project

To leave the demo standing without it spending: pause investigations (`TF_VAR_investigations_paused=true`), switch the budget cutoff on (`TF_VAR_budget_cutoff_enabled=true`), deploy, and leave `MONTHLY_EVALUATION` unset. Nothing then calls a model unless someone resumes it.
