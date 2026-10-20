namespace PaymentOps.Data.Demo.Dataset;

/// <summary>The dataset can't be used. Every problem found is listed, so one fix-and-retry covers them all.</summary>
internal sealed class InvalidDemoDatasetException(IReadOnlyList<string> errors)
    : Exception("The demo dataset is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
