using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Model;

namespace PaymentOps.Agent.Safety;

/// <summary>What screening found in a question before the agent saw it.</summary>
public enum QuestionVerdict
{
    Clean,

    /// <summary>Hate, sexual content or violence: blocked with the content safety notice.</summary>
    Harmful,

    /// <summary>Self-harm: blocked with a notice that points to support.</summary>
    SelfHarm,

    /// <summary>An attempt to override the assistant's instructions.</summary>
    Attack,

    /// <summary>A check couldn't answer in time; the question isn't investigated.</summary>
    Unavailable,
}

/// <summary>Screens each question and follow-up before any model request is made for it.</summary>
public interface IQuestionScreen
{
    /// <summary>Only the caller's cancellation throws; a service failure is <see cref="QuestionVerdict.Unavailable"/>.</summary>
    Task<QuestionVerdict> ScreenAsync(string question, CancellationToken cancellationToken);
}

/// <summary>Used when no Content Safety endpoint is configured: nothing can be checked, so nothing is investigated.</summary>
internal sealed class UnavailableQuestionScreen : IQuestionScreen
{
    public Task<QuestionVerdict> ScreenAsync(string question, CancellationToken cancellationToken) => Task.FromResult(QuestionVerdict.Unavailable);
}

/// <summary>
/// Content Safety's harm analysis for the four categories. Low severity (2) and above is harmful, the same level the
/// chat deployment's strict filter blocks; self-harm is told apart so its notice can point to support.
/// </summary>
internal static class HarmAnalysis
{
    public const int BlockingSeverity = 2;

    private static readonly string[] Categories = ["Hate", "Sexual", "Violence", "SelfHarm"];

    /// <summary>The severity of each category, or null when the text couldn't be checked.</summary>
    public static async Task<IReadOnlyDictionary<string, int>?> AnalyzeAsync(ContentSafetyClient client, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        // Longer text goes in overlapping pieces, as for Prompt Shields; the worst piece decides.
        var pieces = await Task.WhenAll(PromptShieldsScreen.Split(text).Select(piece => PieceAsync(client, piece, cancellationToken)));
        return pieces.Length == 0 ? Categories.ToDictionary(c => c, _ => 0, StringComparer.Ordinal)
            : pieces.Any(p => p is null) ? null
            : Categories.ToDictionary(c => c, c => pieces.Max(p => p!.GetValueOrDefault(c)), StringComparer.Ordinal);
    }

    public static QuestionVerdict Verdict(IReadOnlyDictionary<string, int> severities)
    {
        ArgumentNullException.ThrowIfNull(severities);
        return severities.GetValueOrDefault("SelfHarm") >= BlockingSeverity ? QuestionVerdict.SelfHarm
            : severities.Values.Any(s => s >= BlockingSeverity) ? QuestionVerdict.Harmful
            : QuestionVerdict.Clean;
    }

    private static async Task<Dictionary<string, int>?> PieceAsync(ContentSafetyClient client, string piece, CancellationToken cancellationToken)
    {
        using var body = await client.PostAsync("contentsafety/text:analyze", new { text = piece, categories = Categories, outputType = "FourSeverityLevels" }, cancellationToken);
        if (body?.RootElement.TryGetProperty("categoriesAnalysis", out var analysis) != true || analysis.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var severities = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in analysis.EnumerateArray())
        {
            if (!entry.TryGetProperty("category", out var category) || !entry.TryGetProperty("severity", out var severity) || !severity.TryGetInt32(out var value))
            {
                return null;
            }

            severities[category.GetString() ?? string.Empty] = value;
        }

        return Categories.All(severities.ContainsKey) ? severities : null;
    }
}

/// <summary>
/// Our own check for the four harm categories: one structured call that sees only the question, with no tools and no
/// history. It catches mild phrasings Azure's classifiers score as safe. A call the content filter refuses is a block.
/// </summary>
internal sealed class HarmClassifier(IChatClient model, string instructions)
{
    public const string PromptId = "harm-screen";

    private static readonly Dictionary<string, QuestionVerdict> Verdicts = new(StringComparer.Ordinal)
    {
        ["hate"] = QuestionVerdict.Harmful,
        ["sexual"] = QuestionVerdict.Harmful,
        ["violence"] = QuestionVerdict.Harmful,
        ["self_harm"] = QuestionVerdict.SelfHarm,
    };

    public async Task<QuestionVerdict> ClassifyAsync(string question, CancellationToken cancellationToken)
    {
        ChatResponse response;
        try
        {
            response = await model.GetResponseAsync(
                [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, question)],
                new ChatOptions { ResponseFormat = ChatResponseFormat.Json, MaxOutputTokens = 50, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } },
                cancellationToken);
        }
        catch (Exception ex) when (ex is ContentFilteredException || ModelFailure.IsContentFilterRejection(ModelFailure.LastAttempt(ex)))
        {
            return QuestionVerdict.Harmful;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && (ex is OperationCanceledException || ModelFailure.IsServiceFailure(ex)))
        {
            return QuestionVerdict.Unavailable;
        }

        try
        {
            using var json = JsonDocument.Parse(response.Text);
            var categories = json.RootElement.GetProperty("categories").EnumerateArray().Select(c => c.GetString() ?? string.Empty).ToList();

            // An answer outside the four categories means the call didn't do its job, so it can't clear the question.
            return categories.Any(c => !Verdicts.ContainsKey(c)) ? QuestionVerdict.Unavailable
                : categories.Contains("self_harm") ? QuestionVerdict.SelfHarm
                : categories.Count > 0 ? QuestionVerdict.Harmful
                : QuestionVerdict.Clean;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return QuestionVerdict.Unavailable;
        }
    }
}

/// <summary>
/// The three checks on a question, run together under one budget of 8 s: Content Safety's harm analysis, Prompt Shields'
/// user-prompt attack check, and our classifier. Anything found blocks; any check that can't answer leaves the
/// question unscreened, so it isn't investigated.
/// </summary>
internal sealed class ContentSafetyQuestionScreen(ContentSafetyClient client, HarmClassifier classifier, IOptions<InvestigationLimits> options, TimeProvider time) : IQuestionScreen
{
    public async Task<QuestionVerdict> ScreenAsync(string question, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(options.Value.QuestionScreenTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var checks = new[] { HarmAsync(question, linked.Token), AttackAsync(question, linked.Token), Bounded(classifier.ClassifyAsync(question, linked.Token)) };

        QuestionVerdict[] verdicts;
        try
        {
            verdicts = await Task.WhenAll(checks);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            verdicts = [.. checks.Select(c => c.IsCompletedSuccessfully ? c.Result : QuestionVerdict.Unavailable)];
        }

        return Combine(verdicts);
    }

    /// <summary>Self-harm first, for its notice; then harm, then attack; a finding stands whatever else couldn't answer.</summary>
    public static QuestionVerdict Combine(IReadOnlyList<QuestionVerdict> verdicts) =>
        new[] { QuestionVerdict.SelfHarm, QuestionVerdict.Harmful, QuestionVerdict.Attack, QuestionVerdict.Unavailable }.FirstOrDefault(verdicts.Contains, QuestionVerdict.Clean);

    private async Task<QuestionVerdict> HarmAsync(string question, CancellationToken cancellationToken) =>
        await HarmAnalysis.AnalyzeAsync(client, question, cancellationToken) is { } severities ? HarmAnalysis.Verdict(severities) : QuestionVerdict.Unavailable;

    private async Task<QuestionVerdict> AttackAsync(string question, CancellationToken cancellationToken)
    {
        using var body = await client.PostAsync("contentsafety/text:shieldPrompt", new { userPrompt = question, documents = Array.Empty<string>() }, cancellationToken);
        return body?.RootElement.TryGetProperty("userPromptAnalysis", out var analysis) == true && analysis.TryGetProperty("attackDetected", out var detected)
            && detected.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? detected.GetBoolean() ? QuestionVerdict.Attack : QuestionVerdict.Clean
            : QuestionVerdict.Unavailable;
    }

    // The budget's cancellation reaches the classifier as an exception; it means the check couldn't answer in time.
    private static async Task<QuestionVerdict> Bounded(Task<QuestionVerdict> check)
    {
        try
        {
            return await check;
        }
        catch (OperationCanceledException)
        {
            return QuestionVerdict.Unavailable;
        }
    }
}
