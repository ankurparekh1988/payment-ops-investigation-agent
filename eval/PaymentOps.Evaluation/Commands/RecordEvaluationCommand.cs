using Azure.Identity;

using PaymentOps.Evaluation.Recording;
using PaymentOps.Evaluation.Reporting;

namespace PaymentOps.Evaluation.Commands;

/// <summary>
/// record-evaluation: writes a main-branch evaluation's reports to the evaluation history in Log Analytics, signed in
/// as whoever ran <c>az login</c>. Pull-request results stay on the pull request, so only these triggers are accepted.
/// </summary>
internal static class RecordEvaluationCommand
{
    public const string Usage =
        "record-evaluation --investigations <report.json> --retrieval <report.json> --ingestion-endpoint <url> --rule <immutable id> " +
        "--evaluation-id <id> --trigger schedule|workflow_dispatch";

    private static readonly string[] Valued = ["investigations", "retrieval", "ingestion-endpoint", "rule", "evaluation-id", "trigger"];
    private static readonly string[] Triggers = ["schedule", "workflow_dispatch"];

    public static Task<int> RunAsync(string[] args) => RunAsync(args, null);

    /// <param name="sinks">Tests only: where the rows go; by default the Logs Ingestion API.</param>
    public static Task<int> RunAsync(string[] args, Func<IEvaluationSink>? sinks) => ExitCodes.GuardAsync("Evaluation history", Usage, async () =>
    {
        var options = Options.Parse(args, Valued, []);
        var endpoint = options.Https("ingestion-endpoint");
        var rule = options.One("rule");
        var evaluationId = options.One("evaluation-id");
        var trigger = options.One("trigger");
        if (!Triggers.Contains(trigger, StringComparer.Ordinal))
        {
            throw new UsageException("--trigger must be schedule or workflow_dispatch; pull-request evaluations aren't recorded.");
        }

        var rows = EvaluationRows.From(
            EvaluationReport.Load(options.One("investigations")), EvaluationReport.Load(options.One("retrieval")), evaluationId, trigger, DateTimeOffset.UtcNow);
        var sink = sinks?.Invoke() ?? new LogsIngestionSink(endpoint, rule, new AzureCliCredential());
        await sink.WriteAsync(rows, CancellationToken.None);
        Console.WriteLine($"Recorded {rows.Count} rows for evaluation {evaluationId}.");
        return ExitCodes.Passed;
    });
}
