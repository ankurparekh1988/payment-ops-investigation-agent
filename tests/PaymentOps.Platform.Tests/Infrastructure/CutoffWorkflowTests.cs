using System.Text.Json;
using System.Text.Json.Nodes;

using PaymentOps.Tests.Shared;

namespace PaymentOps.Platform.Tests.Infrastructure;

/// <summary>
/// The budget cutoff's workflow, read from the definition Terraform deploys: it acts only on this budget's own
/// notification once spending has reached the budget, on the one account Terraform names, reading before it writes,
/// and its only write turns network access off. Payloads follow Microsoft's sample for a budget's action-group call.
/// </summary>
public sealed class CutoffWorkflowTests
{
    private static readonly JsonObject Definition = (JsonObject)JsonNode.Parse(File.ReadAllText(
        Path.Combine(RepositoryRoot.FullPath, "infra", "modules", "cost-guardrail", "cutoff-workflow.json")))!;

    private static readonly Dictionary<string, string> Parameters = new(StringComparer.Ordinal)
    {
        ["accountId"] = "/subscriptions/sub-1/resourceGroups/rg-pay-dev/providers/Microsoft.CognitiveServices/accounts/aif-pay-dev",
        ["subscriptionId"] = "sub-1",
        ["budgetName"] = "budget-pay-dev",
        ["resourceGroup"] = "rg-pay-dev",
    };

    private static JsonObject Actions => (JsonObject)Definition["actions"]!;

    private static JsonObject Validation => (JsonObject)Actions["Validate_notification"]!;

    private static IEnumerable<(string Name, JsonObject Action)> All(JsonObject? actions) =>
        actions is null ? [] : actions.SelectMany(a =>
        {
            var action = (JsonObject)a.Value!;
            return new[] { (a.Key, action) }
                .Concat(All(action["actions"] as JsonObject))
                .Concat(All(action["else"]?["actions"] as JsonObject));
        });

    private static IEnumerable<(string Name, JsonObject Action)> Requests => All(Actions).Where(a => (string?)a.Action["type"] == "Http");

    private static string Payload(
        string threshold = "1",
        string spending = "31",
        string budget = "30",
        string budgetName = "budget-pay-dev",
        string subscription = "sub-1",
        string resourceGroup = "rg-pay-dev",
        string budgetType = "Cost",
        string schemaId = "AIP Budget Notification",
        bool withThreshold = true)
    {
        var data = new Dictionary<string, string>
        {
            ["SubscriptionName"] = "Demo",
            ["SubscriptionId"] = subscription,
            ["SpendingAmount"] = spending,
            ["BudgetStartDate"] = "11/1/2026",
            ["Budget"] = budget,
            ["Unit"] = "CAD",
            ["BudgetCreator"] = "owner",
            ["BudgetName"] = budgetName,
            ["BudgetType"] = budgetType,
            ["ResourceGroup"] = resourceGroup,
        };
        if (withThreshold)
        {
            data["NotificationThresholdAmount"] = threshold;
        }

        return JsonSerializer.Serialize(new { schemaId, data });
    }

    private static bool Passes(string payload)
    {
        using var body = JsonDocument.Parse(payload);
        try
        {
            return LogicAppExpression.Evaluate((string)Validation["expression"]!, body.RootElement, Parameters) is true;
        }
        catch (InvalidOperationException)
        {
            // A failed expression fails the run before any action, which is as safe as a skip.
            return false;
        }
    }

    public static TheoryData<string, string, bool> Notifications() => new()
    {
        { "100% actual, spending past the budget", Payload(), true },
        { "100% actual, spending exactly the budget", Payload(spending: "30"), true },
        { "80% actual", Payload(threshold: "0.8", spending: "24"), false },
        { "threshold read as a percentage, spending under the budget", Payload(threshold: "100", spending: "24"), false },
        { "another budget", Payload(budgetName: "budget-other"), false },
        { "another subscription", Payload(subscription: "sub-2"), false },
        { "another resource group", Payload(resourceGroup: "rg-other"), false },
        { "a usage budget", Payload(budgetType: "Usage"), false },
        { "spending that isn't a number", Payload(spending: "thirty-one"), false },
        { "a budget that isn't a number", Payload(budget: "n/a"), false },
        { "no threshold", Payload(withThreshold: false), false },
        { "another schema", Payload(schemaId: "azureMonitorCommonAlertSchema"), false },
    };

    [Theory]
    [MemberData(nameof(Notifications))]
    public void Only_this_budgets_notification_at_its_limit_passes(string row, string payload, bool passes) =>
        Assert.True(passes == Passes(payload), row);

    [Fact]
    public void The_trigger_rejects_a_payload_that_breaks_its_schema()
    {
        var trigger = (JsonObject)Definition["triggers"]!.AsObject().Single().Value!;
        var data = trigger["inputs"]!["schema"]!["properties"]!["data"]!;

        Assert.Equal("EnableSchemaValidation", (string?)trigger["operationOptions"]);
        Assert.Equal(["schemaId", "data"], trigger["inputs"]!["schema"]!["required"]!.AsArray().Select(n => (string)n!));
        Assert.Contains("NotificationThresholdAmount", data["required"]!.AsArray().Select(n => (string)n!));
        Assert.Contains("SpendingAmount", data["required"]!.AsArray().Select(n => (string)n!));
    }

    [Fact]
    public void Every_request_sits_behind_the_validation()
    {
        Assert.Equal("If", (string?)Validation["type"]);
        Assert.Single(Actions);
        Assert.All(Requests, r => Assert.Contains(r.Name, All(Validation["actions"] as JsonObject).Select(a => a.Name)));
        Assert.DoesNotContain(All(Validation["else"]?["actions"] as JsonObject), a => (string?)a.Action["type"] != "Terminate");
    }

    [Fact]
    public void The_target_is_the_parameter_never_the_payload()
    {
        Assert.NotEmpty(Requests);
        Assert.All(Requests, r =>
        {
            var uri = (string)r.Action["inputs"]!["uri"]!;
            Assert.Contains("parameters('accountId')", uri, StringComparison.Ordinal);
            Assert.DoesNotContain("trigger", uri, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("ManagedServiceIdentity", (string?)r.Action["inputs"]!["authentication"]!["type"]);
        });
    }

    [Fact]
    public void It_reads_before_it_writes_and_skips_when_already_disabled()
    {
        var read = Assert.Single(Requests, r => (string?)r.Action["inputs"]!["method"] == "GET");
        var check = All(Validation["actions"] as JsonObject).Single(a => a.Name == "Skip_if_already_disabled").Action;

        Assert.Equal(["Succeeded"], check["runAfter"]![read.Name]!.AsArray().Select(n => (string)n!));
        Assert.Contains("'Disabled'", (string)check["expression"]!, StringComparison.Ordinal);
        Assert.Equal("Terminate", (string?)check["actions"]!.AsObject().Single().Value!["type"]);
        Assert.Contains(All(check["else"]!["actions"] as JsonObject), a => (string?)a.Action["inputs"]?["method"] == "PATCH");
    }

    [Fact]
    public void Its_only_write_turns_network_access_off()
    {
        var write = Assert.Single(Requests, r => (string?)r.Action["inputs"]!["method"] != "GET");

        Assert.Equal("PATCH", (string?)write.Action["inputs"]!["method"]);
        Assert.Equal("""{"properties":{"publicNetworkAccess":"Disabled"}}""", write.Action["inputs"]!["body"]!.ToJsonString());
    }

    [Fact]
    public void A_failure_records_the_status_code_not_the_payload()
    {
        var failed = All(Validation["actions"] as JsonObject).Single(a => a.Name == "Record_failure").Action;
        var recorded = failed.ToJsonString();

        Assert.Equal("Failed", (string?)failed["inputs"]!["runStatus"]);
        Assert.DoesNotContain("triggerBody", recorded, StringComparison.Ordinal);
        Assert.Contains("statusCode", recorded, StringComparison.Ordinal);
        Assert.All(All(Actions), a => Assert.DoesNotContain("triggerBody", a.Action["trackedProperties"]?.ToJsonString() ?? string.Empty, StringComparison.Ordinal));
    }
}
