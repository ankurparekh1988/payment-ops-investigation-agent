namespace PaymentOps.Agent.Contracts;

/// <param name="Questions">Questions recorded in this conversation.</param>
/// <param name="CarriedQuestions">How many of them the next question would carry as context.</param>
/// <param name="Ended">No further question fits; a new investigation starts over.</param>
public sealed record ConversationStatus(int Questions, int CarriedQuestions, bool Ended);
