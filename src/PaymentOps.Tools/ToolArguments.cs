using System.Globalization;
using System.Text.RegularExpressions;

namespace PaymentOps.Tools;

/// <summary>An inclusive range of dates as the model gives it, with the half-open forms the ports take.</summary>
public readonly record struct DateRange(DateOnly Start, DateOnly End)
{
    public DateOnly EndExclusive => End.AddDays(1);

    public DateTimeOffset FromUtc => new(Start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    public DateTimeOffset ToUtcExclusive => new(EndExclusive.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}

/// <summary>Validation shared by every tool, done before any data source is called.</summary>
public static partial class ToolArguments
{
    public const int MaxRangeDays = 31;
    public const int MaxIdLength = 64;

    // Ids are opaque and matched exactly, so case is kept; only surrounding whitespace is dropped.
    public static bool TryId(string? value, string name, out string id, out string? error)
    {
        id = value?.Trim() ?? string.Empty;
        error = id.Length == 0 ? $"{name} is required."
            : id.Length > MaxIdLength ? $"{name} can be at most {MaxIdLength} characters."
            : !IdPattern().IsMatch(id) ? $"{name} must be an id such as M-1042 or fabrikam-pay."
            : null;
        return error is null;
    }

    public static bool TryRange(string? start, string? end, DateOnly today, out DateRange range, out ToolErrorCode code, out string? error)
    {
        range = default;
        code = ToolErrorCode.InvalidArgument;
        if (!TryDate(start, out var from) || !TryDate(end, out var to))
        {
            error = "start_date and end_date must be dates written as YYYY-MM-DD.";
            return false;
        }

        error = from > to ? "start_date can't be later than end_date."
            : to > today ? $"end_date can't be later than {today:yyyy-MM-dd}."
            : null;
        if (error is null && to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            code = ToolErrorCode.RangeTooLarge;
            error = $"The range can be at most {MaxRangeDays} days.";
        }

        range = new DateRange(from, to);
        return error is null;
    }

    /// <summary>The id, or a run that ends with the validation message.</summary>
    internal static string Id(string? value, string name) =>
        TryId(value, name, out var id, out var error) ? id : throw new ToolFailedException(new(ToolErrorCode.InvalidArgument, error!));

    internal static DateRange Range(string? start, string? end, DateOnly today) =>
        TryRange(start, end, today, out var range, out var code, out var error) ? range : throw new ToolFailedException(new(code, error!));

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
