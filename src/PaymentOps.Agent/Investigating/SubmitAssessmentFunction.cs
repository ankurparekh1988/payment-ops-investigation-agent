using System.Text.Json;

using Microsoft.Extensions.AI;

using PaymentOps.Tools;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// The explicit end of an investigation. The submission is validated here, inside the loop, so a failure goes
/// back to the model as this tool's result; either way the loop ends, and the runner decides whether the one
/// correction is still allowed.
/// </summary>
internal sealed class SubmitAssessmentFunction(TurnState turn) : AIFunction
{
    public const string ToolName = "submit_assessment";

    private static readonly string Accepted = JsonSerializer.Serialize(new { Status = "accepted" }, ToolJson.Options);

    public override string Name => ToolName;

    public override string Description =>
        "Submit the final assessment. Call it exactly once, when the investigation is done; every statement lists the evidence ids and citation ids that support it.";

    public override JsonElement JsonSchema => AssessmentSchema.Parameters;

    // Strict schema on the wire, so the model can't add or leave out fields; the server checks again anyway.
    public override IReadOnlyDictionary<string, object?> AdditionalProperties { get; } = new Dictionary<string, object?> { ["strict"] = true };

    public AssessmentSubmission? Submission { get; private set; }

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (FunctionInvokingChatClient.CurrentContext is { } context)
        {
            context.Terminate = true;
        }

        // A second call in the same response changes nothing once one was accepted.
        if (Submission is not null)
        {
            return new(Accepted);
        }

        var submission = AssessmentSubmission.Parse(JsonSerializer.SerializeToElement(arguments, ToolJson.Options));
        IReadOnlyList<string> failures = submission is null
            ? ["The submission doesn't match the submit_assessment parameters."]
            : AssessmentValidator.Check(submission, turn);
        if (failures.Count == 0)
        {
            Submission = submission;
            turn.Finish();
            return new(Accepted);
        }

        return new(JsonSerializer.Serialize(
            new
            {
                Status = "rejected",
                Failures = failures,
                Instruction = "Correct these and call submit_assessment again. No other tools are available now.",
            },
            ToolJson.Options));
    }
}
