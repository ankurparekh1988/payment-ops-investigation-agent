# Payment Ops Investigation Agent

[![CI](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/ci.yml/badge.svg)](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/ci.yml)
[![CD](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/cd.yml/badge.svg)](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/cd.yml)

An AI agent that helps a payments operations team work out why money didn't move when it should have: a late payout, a missing settlement, a processor outage.

> 🚧 **Early stage.** The foundation is in place: module structure, architecture tests, the web host with Entra sign-in and app roles, and the Azure platform (models, hosting, storage, monitoring) defined in Terraform, with CI checking every change and an approval-gated pipeline deploying it. The rest of this page describes the design it's being built towards.

When a merchant's payout is late, the explanation is usually spread across settlement records, error logs, a processor's status page and a runbook, and someone has to piece it together by hand. The agent is designed to do that legwork. Ask it *"Why were payouts for merchant M-1042 delayed yesterday?"* and it will pull the relevant data and documents, explain what happened, and show exactly where each fact came from.

The interesting problems are less about the model and more about everything around it:

- **Not everyone should see everything.** Restricted documents will be filtered out before the model ever sees them, based on who is asking.
- **Answers need to be checkable.** Every claim will link back to its source, and the server will reject references to anything that wasn't actually retrieved.
- **The agent suggests; people decide.** It can propose an incident ticket, but an engineer has to approve it.
- **Changes need proof.** A prompt or model change only ships if it passes an evaluation run.

The setting is a fictional company, *Contoso Payments*, and all data is synthetic.

## Tech

.NET 10 and Blazor, with Microsoft Agent Framework for the agent. Azure: Microsoft Foundry, Azure AI Search and Entra ID. Infrastructure uses Terraform, deployed through GitHub Actions.

More detail: [product notes](docs/PRD.md) · [architecture so far](docs/Architecture.md) · [deployment](docs/Deployment.md) · [security](docs/Security.md) · [decisions](docs/adr/README.md)

## Running locally

You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build PaymentOps.slnx
dotnet test PaymentOps.slnx
dotnet run --project src/PaymentOps.Web
```

Without sign-in settings the health checks answer and pages return 503. To sign in locally, see [Running locally with sign-in](docs/Deployment.md#running-locally-with-sign-in).

## License

[MIT](LICENSE)
