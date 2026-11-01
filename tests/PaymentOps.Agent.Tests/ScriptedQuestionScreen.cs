using PaymentOps.Agent.Safety;

namespace PaymentOps.Agent.Tests;

/// <summary>Answers every question with one verdict and records what it was asked.</summary>
internal sealed class ScriptedQuestionScreen(QuestionVerdict verdict) : IQuestionScreen
{
    public static ScriptedQuestionScreen AllClean => new(QuestionVerdict.Clean);

    public List<string> Screened { get; } = [];

    public Task<QuestionVerdict> ScreenAsync(string question, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Screened)
        {
            Screened.Add(question);
        }

        return Task.FromResult(verdict);
    }
}
