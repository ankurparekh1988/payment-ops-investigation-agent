# Running it against your own Azure subscription

The build and the tests run offline. The app itself needs Azure: Entra ID for sign-in, Foundry for the models, AI Search for the runbooks and Content Safety for screening. This guide creates all of it in your subscription from your own machine and configures the app to run on `https://localhost:7207`. There's no GitHub, no pipeline and no approval step. The repository's own environment is deployed by its pipeline instead, as described in [Deployment](docs/Deployment.md).

## Before you start

- An Azure subscription where you're **Owner**.
- Rights to create an app registration and a security group in Entra ID. Most new or personal tenants allow this; otherwise you need Application Administrator and Groups Administrator, plus User Administrator for the optional demo users.
- Quota in your region for the models in [`ai/manifest.yaml`](ai/manifest.yaml): `gpt-5.4-mini`, `gpt-5.4-nano` and `text-embedding-3-small`, Global Standard. The script checks this before creating any model and stops with what's missing.
- Installed: [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), [Terraform](https://developer.hashicorp.com/terraform/install) 1.10 or later, the [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) version in `global.json`, [jq](https://jqlang.org/download/) and Bash (on Windows, Git Bash).

## Four steps

```bash
cp .env.local.example .env       # then fill in: region, name prefix, alert email
az login
scripts/setup-local.sh
dotnet run --project src/PaymentOps.Web --launch-profile https
```

Open `https://localhost:7207` and sign in with your own account, which has the Admin role. The app reaches Azure as your `az login` account; the launch profile limits it to the Azure CLI's sign-in, so other signed-in tools such as Visual Studio aren't tried first. Rerunning the script is safe: if a step fails part-way, for example while new permissions take effect, run it again and it carries on.

## What the script creates

| Service | What the app uses it for | Without it |
|---|---|---|
| Entra ID app registration | Sign-in and the Reader, Engineer and Admin roles | Every page returns 503 |
| Foundry (models) | The models the agent and the question screen call | Investigations report that the model isn't responding |
| Content Safety, on the same Foundry account | Screening questions and untrusted tool text | No question can be screened, so none is investigated |
| AI Search | The runbooks the agent cites | Knowledge search fails and runbook evidence shows as a gap |
| Storage, monitoring, budget, web app | Terraform state, telemetry, spending alerts. The web app stays empty: the pipeline deploys it | Nothing local depends on them |

The defaults are the free or lowest tiers, and a monthly budget emails your alert address. The models and Content Safety are billed per call, so spending follows how many investigations you run. Change any tier in `.env` before running the script.

The script writes the app's settings to [.NET user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), outside the repository. That includes a sign-in secret that expires after 30 days; rerun the script to replace it.

## Things that can stop it

- **Model quota.** If the region lacks quota or capacity, the script stops before deploying any model. Choose another region in `.env`, or request quota in the Foundry portal.
- **One free Search service per subscription.** If you already have one, the script stops before creating anything. Set `TF_VAR_search_sku=basic` in `.env` (billed hourly), or remove the other service.
- **An existing `.env` for a pipeline environment.** Local mode refuses to run when GitHub values are configured, so it can't change an environment its pipeline owns. Use a separate clone or `.env`.

## Removing it

```bash
scripts/teardown-local.sh --all  # asks you to type the name prefix
```

That removes everything the setup created: the services, the app registration and the bootstrap (Terraform state storage, resource groups, custom roles and the cost anomaly alert). Without `--all` the bootstrap stays, costing next to nothing, so the setup can run again sooner. Azure keeps a deleted Log Analytics workspace recoverable for 14 days and Entra keeps deleted objects for 30, neither billed.
