namespace PaymentOps.Web;

/// <summary>The answering limits the UI shows people; the service enforces them.</summary>
internal sealed record AnsweringLimits(int MaxQuestionLength);
