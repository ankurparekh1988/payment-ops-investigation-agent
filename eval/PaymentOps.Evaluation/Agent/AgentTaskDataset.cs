using System.Text.Json;
using System.Text.Json.Serialization;

using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Agent;

/// <param name="Access">"general" or "restricted": which knowledge the simulated Ops Reader may see.</param>
/// <param name="IntendedTools">What a person following the runbooks would use; recorded against, never required.</param>
internal sealed record AgentTask(string Id, string Scenario, string Access, string Question, IReadOnlyList<string> IntendedTools);

internal static class AgentTaskDataset
{
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public static IReadOnlyList<AgentTask> Load(string path)
    {
        var file = JsonSerializer.Deserialize<TaskFile>(File.ReadAllText(path), Strict) ?? throw new InvalidDataException($"{path} is empty.");
        var problems = file.Tasks
            .SelectMany(t => t.IntendedTools.Where(tool => !ToolNames.ReadTools.Contains(tool)).Select(tool => $"{t.Id}: unknown tool '{tool}'."))
            .Concat(file.Tasks.Where(t => t.Access is not ("general" or "restricted")).Select(t => $"{t.Id}: access must be general or restricted."))
            .Concat(file.Tasks.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => $"{g.Key}: appears more than once."))
            .ToList();
        return problems.Count == 0 ? file.Tasks : throw new InvalidDataException(string.Join(' ', problems));
    }

    private sealed record TaskFile(string Description, IReadOnlyList<AgentTask> Tasks);
}
