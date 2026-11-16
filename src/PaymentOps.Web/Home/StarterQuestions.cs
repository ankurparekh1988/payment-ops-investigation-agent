using System.Globalization;

namespace PaymentOps.Web.Home;

/// <summary>Fixed example questions over the demo data, offered on the home page.</summary>
public static class StarterQuestions
{
    public static IReadOnlyList<string> All { get; } =
    [
        "Why were payouts for merchant M-1042 delayed yesterday?",
        "Why is M-2087's payout for 4 Nov held?",
        "Was M-1042 affected by the Fabrikam Pay incident on 9 Nov?",
    ];
}

/// <summary>The question an attention row hands to Investigate, written from the record's own fields.</summary>
public static class AttentionQuestion
{
    public static string HeldPayout(string merchantId, DateOnly batchDate) =>
        string.Create(CultureInfo.InvariantCulture, $"Why is {merchantId}'s payout for {batchDate:d MMM} held?");

    public static string RejectedFile(string processorId, DateOnly batchDate) =>
        string.Create(CultureInfo.InvariantCulture, $"Why was {processorId}'s settlement file for {batchDate:d MMM} rejected?");
}
