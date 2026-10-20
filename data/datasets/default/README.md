# Default demo dataset

A synthetic snapshot of the systems of record the investigation agent queries: merchants and their risk holds, payments, settlement files, payouts, the settlement calendar, processor configuration and incidents, and our own error events. Everything here is invented.

`dataset.json` is built into `PaymentOps.Data.Demo` and checked when the app starts. A broken reference, amounts that don't add up, a payout due on a non-settlement day or paid before its file arrived, a non-UTC time, or anything dated after the as-of time stops startup, with every problem listed.

## The as-of time

The data is a snapshot taken at **Wednesday 11 November 2026, 06:00 UTC**, before that day's payout runs. Investigations reason from that moment, so "yesterday" means Tuesday 10 November however long ago the dataset was written.

| Processor | Cut-off | Files expected | Funds released |
|---|---|---|---|
| `fabrikam-pay` | 16:00 New York (21:00 UTC in November) | 02:00 New York | At the 14:00 UTC run on the first settlement day after the batch once its files are in |
| `northwind-acquiring` | 17:00 London (17:00 UTC in November) | 03:00 London | At the 09:00 UTC run two settlement days after the batch |

Friday 6 November is a settlement system closure day, followed by the weekend.

## Scenarios

| Scenario | Where to look | What the data shows |
|---|---|---|
| Payouts delayed by a processor outage | `M-1042`, Monday 9 November's batch, due yesterday | Fabrikam payments captured before the cut-off; Fabrikam's settlement incident `INC-FAB-1109` started before the cut-off and is still open; repeated file transfer timeouts from Fabrikam only; no Fabrikam file for the 9th. Northwind's file for the same day arrived, and its payout `PO-1042-NW-1109` is due on Northwind's normal schedule |
| A missed cut-off before a closure day | `M-6230`, `PAY-6230-04` | Captured five minutes after Northwind's cut-off on Thursday 5 November. The closure day and the weekend move its batch to Monday 9, and the holding period makes payout `PO-6230-NW-1109` due today, Wednesday 11: later than the merchant expects, but on time |
| A risk hold | `M-2087` | An `RF-37` hold placed on 2 November. Payments and files carry on; every payout is `Held` |
| A duplicate settlement batch | Fabrikam, batch date 5 November | `SF-FAB-1105`, a failed acknowledgement from us (`EV-1106-01`), then `SF-FAB-1105-R` with the same payments, resent on Tuesday after that day's payout run. Each merchant has a paid payout from the first file and a second payout due today from the resend |

Each also has something nearby that looks related but isn't:
- **Processor outage:** an earlier Fabrikam incident that affected authorizations only.
- **Missed cut-off:** captures before the cut-off on the same Thursday, paid on time.
- **Risk hold:** a Fabrikam file rejected on 5 November and corrected after that day's payout run, so `M-3310` and `M-4521` were paid one settlement day late for an unrelated reason.
- **Duplicate batch:** Fabrikam's batch for 2 November, split across two files with different payments, which is normal for Fabrikam. Telling the two apart means comparing the payments, not just counting files.

## Changing the data

Edit `dataset.json` directly and run `dotnet test tests/PaymentOps.Data.ContractTests`. The loader tests report any broken reference, and the scenario tests fail if a change stops a scenario from being solvable.

Optional fields are written out as `null`, never left out. Times are UTC with a `Z`. A file line's amount is the sum of its payments, and a payout's is the sum of its merchant's lines in its source files; the loader checks both.
