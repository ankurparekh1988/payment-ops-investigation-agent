using System.Text.Json;
using System.Text.Json.Serialization;

using PaymentOps.Knowledge.Corpus;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Agent;

/// <param name="Access">"general" or "restricted": which knowledge the simulated user may see.</param>
/// <param name="Role">"reader" or "engineer": an engineer's proposal is kept, and the run approves it.</param>
/// <param name="IntendedTools">What a person following the runbooks would use; recorded against, never required.</param>
/// <param name="ExpectProposal">A runbook calls for an incident here; recorded against, never required.</param>
internal sealed record AgentTask(string Id, string Scenario, string Access, string Role, string Question, IReadOnlyList<string> IntendedTools, bool ExpectProposal)
{
    public const string Reader = "reader";
    public const string Engineer = "engineer";
}

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
            .Concat(file.Tasks.Where(t => t.Access is not (KnowledgeClassification.General or KnowledgeClassification.Restricted)).Select(t => $"{t.Id}: access must be general or restricted."))
            .Concat(file.Tasks.Where(t => t.Role is not (AgentTask.Reader or AgentTask.Engineer)).Select(t => $"{t.Id}: role must be reader or engineer."))
            .Concat(file.Tasks.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => $"{g.Key}: appears more than once."))
            .ToList();
        return problems.Count == 0 ? file.Tasks : throw new InvalidDataException(string.Join(' ', problems));
    }

    private sealed record TaskFile(string Description, IReadOnlyList<AgentTask> Tasks);
}
