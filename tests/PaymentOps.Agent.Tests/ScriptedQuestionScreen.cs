using PaymentOps.Agent.Safety;

namespace PaymentOps.Agent.Tests;

/// <summary>Answers each question with the next verdict, repeating the last, and records what it was asked.</summary>
internal sealed class ScriptedQuestionScreen(params QuestionVerdict[] verdicts) : IQuestionScreen
{
    public static ScriptedQuestionScreen AllClean => new(QuestionVerdict.Clean);

    public List<string> Screened { get; } = [];

    public Task<QuestionVerdict> ScreenAsync(string question, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Screened)
        {
            Screened.Add(question);
            return Task.FromResult(verdicts[Math.Min(Screened.Count, verdicts.Length) - 1]);
        }
    }
}
