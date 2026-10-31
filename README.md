# Payment Ops Investigation Agent

[![CI](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/ci.yml/badge.svg)](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/ci.yml)
[![CD](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/cd.yml/badge.svg)](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/cd.yml)

An AI agent that helps a payments operations team work out why money didn't move when it should have: a late payout, a missing settlement, a processor outage.

> 🚧 **Early stage.** Signed-in users investigate on the home page: they ask about a merchant's money, watch the agent choose its own read-only tools step by step, and get an assessment whose every statement the server has checked against what those tools returned, drawn only from data and documents they're allowed to see. Follow-up questions carry the earlier ones as context. When a runbook says to raise an incident, the agent can propose a ticket; only the Operations Engineer who ran the investigation can approve it, and the server, not the model, creates it. Text written outside the system, such as incident notes, names and runbooks, is screened with Prompt Shields before the model reads it, and whatever is flagged or can't be checked is withheld; a question the content filter blocks ends with a fixed notice and is never sent again. Underneath: module structure, architecture tests, Entra sign-in and app roles, an AI release manifest that the infrastructure and the app both read, a validated corpus of runbooks and policies, a search index every deployment publishes and verifies, a retrieval strategy chosen by measurement against reviewed cases (lexical), synthetic merchant, settlement and processor data behind domain ports, and the Azure platform defined in Terraform, with CI checking every change and an approval-gated pipeline deploying it. An evaluation suite measures the agent against labelled regression and held-out cases over three runs, against thresholds frozen from a baseline of `main` by a rule written down before it ran; the agent also beat a fixed-sequence workflow on the same cases. Pull requests that change AI behaviour must pass that evaluation in CI, keyless and after approval, and a deliberately degraded prompt was shown to fail it; a weekly run of `main` records its results in Log Analytics. Conversations, proposals and tickets are held in memory for now, and the rest of this page describes the design it's being built towards.

When a merchant's payout is late, the explanation is usually spread across settlement records, error logs, a processor's status page and a runbook, and someone has to piece it together by hand. The agent is designed to do that legwork. Ask it *"Why were payouts for merchant M-1042 delayed yesterday?"* and it will pull the relevant data and documents, explain what happened, and show exactly where each fact came from.

The interesting problems are less about the model and more about everything around it:

- **Not everyone should see everything.** Restricted documents are filtered out before the model ever sees them, based on who is asking.
- **Answers need to be checkable.** Every statement in an assessment lists the records, computed facts and knowledge sections behind it, and the server rejects an assessment that cites anything this investigation's tools didn't return. The evaluation suite measures how often the right records and sections are cited, against human-written labels.
- **Data can carry instructions.** An incident note or a runbook can say "ignore your rules". Every such string is screened before the model sees it, and withheld text can't support anything the agent says.
- **The agent suggests; people decide.** It can propose an incident ticket, but only the engineer who ran the investigation can approve it, and the server re-checks who they are when they click.
- **Changes need proof.** A prompt or model change only ships if it passes an evaluation run.

The setting is a fictional company, *Contoso Payments*, and all data is synthetic. Contoso is a payment orchestration platform: it routes merchants' payments to downstream processors and acquirers and pays merchants out from their settlement files. The agent is an internal tool for Contoso's operations, engineering, admin and risk teams, and merchants never use it. That's why it may see routing, processor responses and internal risk state.

## Tech

.NET 10 and Blazor, with Microsoft Agent Framework for the agent. Azure: Microsoft Foundry, Azure AI Search and Entra ID. Infrastructure uses Terraform, deployed through GitHub Actions.

More detail: [product notes](docs/PRD.md) · [architecture so far](docs/Architecture.md) · [deployment](docs/Deployment.md) · [security](docs/Security.md) · [governance](docs/Governance.md) · [runbook](docs/Runbook.md) · [evaluation](docs/Evaluation.md) · [decisions](docs/adr/README.md)

## Running locally

You'll need the [.NET SDK 10.0.403](https://dotnet.microsoft.com/download/dotnet/10.0), the exact version `global.json` pins. A newer SDK is a deliberate change: its pull request goes through the AI evaluation gate.

```bash
dotnet build PaymentOps.slnx
dotnet test PaymentOps.slnx
dotnet run --project src/PaymentOps.Web
```

Without sign-in settings the health checks answer and pages return 503. Without the Search and Foundry endpoints, investigations report that the service isn't responding; without the Content Safety endpoint, text from tools that needs screening is withheld from the model. To sign in locally, see [Running locally with sign-in](docs/Deployment.md#running-locally-with-sign-in).

## License

[MIT](LICENSE)
