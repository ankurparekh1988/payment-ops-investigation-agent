using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace PaymentOps.Evaluation.Tests;

/// <summary>
/// Which jobs of a workflow run, decided as GitHub decides it: in <c>needs</c> order, each job's <c>if</c> evaluated
/// with an implicit <c>success()</c> unless it calls a status function, <c>success()</c> true only when every job it
/// needs succeeded, and a skipped job's outputs empty. Jobs that run produce the outputs the test supplies; output
/// expressions are evaluated. A condition that reads a job it doesn't need throws, as that job's result would be unknown.
/// </summary>
internal static partial class WorkflowRun
{
    public static Dictionary<string, string> Simulate(
        YamlMappingNode workflow,
        IReadOnlyDictionary<string, object> context,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> outputs)
    {
        var jobs = Jobs(workflow);
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        var produced = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        while (results.Count < jobs.Count)
        {
            var (id, job) = jobs.First(j => !results.ContainsKey(j.Key) && j.Value.Needs.All(results.ContainsKey));
            var condition = job.If is { } written && StatusFunction().IsMatch(written) ? written : $"success() && ({job.If ?? "true"})";
            var scope = new Dictionary<string, object>(context, StringComparer.Ordinal) { ["success()"] = job.Needs.All(n => results[n] == "success") };
            foreach (var read in NeedsRead(condition))
            {
                if (!job.Needs.Contains(read))
                {
                    throw new InvalidOperationException($"{id} reads needs.{read} without needing it");
                }
            }

            foreach (var need in job.Needs)
            {
                scope[$"needs.{need}.result"] = results[need];
                foreach (var (name, value) in produced.GetValueOrDefault(need) ?? [])
                {
                    scope[$"needs.{need}.outputs.{name}"] = value;
                }
            }

            var runs = WorkflowExpression.IsTrue(condition, scope);
            results[id] = runs ? "success" : "skipped";
            if (runs)
            {
                var supplied = outputs.GetValueOrDefault(id) ?? new Dictionary<string, string>();
                produced[id] = job.Outputs.ToDictionary(
                    o => o.Key,
                    o => o.Value.Contains("${{", StringComparison.Ordinal) && !o.Value.Contains("steps.", StringComparison.Ordinal)
                        ? WorkflowExpression.Evaluate(o.Value, context) switch { bool b => b ? "true" : "false", var v => v.ToString() ?? string.Empty }
                        : supplied.GetValueOrDefault(o.Key, string.Empty),
                    StringComparer.Ordinal);
            }
        }

        return results;
    }

    /// <summary>The jobs each job's condition reads through <c>needs</c>.</summary>
    public static IEnumerable<string> NeedsRead(string condition) => NeedsReference().Matches(condition).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal);

    public sealed record Job(IReadOnlyList<string> Needs, string? If, IReadOnlyDictionary<string, string> Outputs);

    public static Dictionary<string, Job> Jobs(YamlMappingNode workflow) =>
        ((YamlMappingNode)workflow["jobs"]).Children.ToDictionary(
            j => ((YamlScalarNode)j.Key).Value!,
            j =>
            {
                var job = (YamlMappingNode)j.Value;
                var needs = job.Children.TryGetValue(new YamlScalarNode("needs"), out var n)
                    ? n is YamlSequenceNode list ? [.. list.Children.Select(c => ((YamlScalarNode)c).Value!)] : [((YamlScalarNode)n).Value!]
                    : Array.Empty<string>();
                var condition = job.Children.TryGetValue(new YamlScalarNode("if"), out var i) ? ((YamlScalarNode)i).Value : null;
                var outputs = job.Children.TryGetValue(new YamlScalarNode("outputs"), out var o)
                    ? ((YamlMappingNode)o).Children.ToDictionary(e => ((YamlScalarNode)e.Key).Value!, e => ((YamlScalarNode)e.Value).Value!, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal);
                return new Job(needs, condition, outputs);
            },
            StringComparer.Ordinal);

    [GeneratedRegex(@"\b(success|always|failure|cancelled)\(\)")]
    private static partial Regex StatusFunction();

    [GeneratedRegex(@"needs\.([A-Za-z0-9_-]+)\.")]
    private static partial Regex NeedsReference();
}
