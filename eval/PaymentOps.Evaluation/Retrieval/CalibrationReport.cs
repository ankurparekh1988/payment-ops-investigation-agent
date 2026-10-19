using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentOps.Evaluation.Retrieval;

/// <summary>One result, two renderings: the JSON artifact and the job summary. Observations and the rule's result are kept apart.</summary>
internal static class CalibrationReport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string ToJson(CalibrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return JsonSerializer.Serialize(
            new
            {
                observations = new { runs = result.Runs, modes = result.Modes, probesPassed = result.ProbesPassed },
                decision = new { recommendedMode = result.RecommendedMode, recommendedFloor = result.RecommendedFloor, floorCandidates = result.FloorCandidates },
            },
            Json);
    }

    public static string ToMarkdown(CalibrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var md = new StringBuilder();
        md.AppendLine("## Retrieval calibration").AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Access probes: **{(result.ProbesPassed ? "passed" : "FAILED")}**").AppendLine();
        md.AppendLine("| Mode | Critical bar | Misses | MRR |").AppendLine("|---|---|---|---|");
        foreach (var mode in result.Modes)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {mode.Mode} | {(mode.PassesCriticalBar ? "pass" : "fail")} | {string.Join(", ", mode.CriticalMisses)} | {mode.Mrr:F3} |");
        }

        md.AppendLine().AppendLine("| Case | Kind | Mode | Expected rank | RR | Top 5 (reranker score) |").AppendLine("|---|---|---|---|---|---|");
        foreach (var run in result.Runs)
        {
            var top = string.Join("<br>", run.Top.Select(h => h.RerankerScore is { } s
                ? string.Create(CultureInfo.InvariantCulture, $"{h.DocId}: {h.SectionPath} ({s:F2})")
                : $"{h.DocId}: {h.SectionPath}"));
            var rank = run.ExpectedRank?.ToString(CultureInfo.InvariantCulture) ?? "-";
            md.AppendLine(CultureInfo.InvariantCulture, $"| {run.CaseId} | {run.Kind} | {run.Mode} | {rank} | {run.ReciprocalRank:F3} | {top} |");
        }

        md.AppendLine().AppendLine("### Rule result").AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Recommended mode: **{result.RecommendedMode}**").AppendLine();
        var floor = result.RecommendedFloor is { } f ? f.ToString("F2", CultureInfo.InvariantCulture) : "none";
        md.AppendLine(CultureInfo.InvariantCulture, $"Recommended floor: **{floor}**").AppendLine();
        md.AppendLine("| Floor | Keeps answerable | Unrelated emptied | Qualifies |").AppendLine("|---|---|---|---|");
        foreach (var candidate in result.FloorCandidates)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {candidate.Floor:F2} | {candidate.KeepsAnswerable} | {candidate.UnrelatedEmptied}/{candidate.UnrelatedTotal} | {candidate.Qualifies} |");
        }

        return md.ToString();
    }
}
