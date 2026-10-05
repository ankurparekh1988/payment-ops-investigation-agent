# Payment Ops Investigation Agent

An AI agent that helps a payments operations team work out why money didn't move when it should have: a late payout, a missing settlement, a processor outage.

When a merchant's payout is late, the explanation is usually spread across settlement records, error logs, a processor's status page and a runbook, and someone has to piece it together by hand. This agent does that legwork. Ask it *"Why were payouts for merchant M-1042 delayed yesterday?"* and it pulls the relevant data and documents, explains what happened, and shows exactly where each fact came from.

The interesting problems are less about the model and more about everything around it:

- **Not everyone should see everything.** Restricted documents are filtered out before the model ever sees them, based on who is asking.
- **Answers need to be checkable.** Every claim links back to its source, and the server rejects references to anything that wasn't actually retrieved.
- **The agent suggests; people decide.** It can propose an incident ticket, but an engineer has to approve it.
- **Changes need proof.** A prompt or model change only ships if it passes an evaluation run.

The setting is a fictional company, *Contoso Payments*, and all data is synthetic.

> 🚧 Work in progress. The project structure is in place, and the Azure platform is next.

## Tech

.NET 10 and Blazor, with Microsoft Agent Framework for the agent. Azure: Microsoft Foundry, Azure AI Search and Entra ID. Infrastructure uses Terraform, deployed through GitHub Actions.

More detail: [product notes](docs/PRD.md) · [architecture so far](docs/Architecture.md)

## Running locally

You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build PaymentOps.slnx
dotnet test PaymentOps.slnx
dotnet run --project src/PaymentOps.Web
```

## License

[MIT](LICENSE)
