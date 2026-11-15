using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PaymentOps.Platform.Tests.Infrastructure;

/// <summary>
/// Evaluates the subset of Logic Apps workflow expressions the budget cutoff's condition uses, so the condition is
/// tested as written: <c>and</c>, <c>equals</c>, <c>greaterOrEquals</c>, <c>float</c>, <c>isFloat</c>, <c>if</c>,
/// <c>parameters('name')</c>, string and number literals, and <c>triggerBody()?['a']?['b']</c>. As in Logic Apps,
/// every argument is evaluated, so <c>float</c> of text that isn't a number throws, and a missing property is null.
/// </summary>
internal sealed partial class LogicAppExpression
{
    private readonly List<string> _tokens;
    private readonly JsonElement _body;
    private readonly IReadOnlyDictionary<string, string> _parameters;
    private int _position;

    private LogicAppExpression(string expression, JsonElement body, IReadOnlyDictionary<string, string> parameters)
    {
        _tokens = [.. Token().Matches(expression.TrimStart('@')).Select(m => m.Value)];
        _body = body;
        _parameters = parameters;
    }

    public static object? Evaluate(string expression, JsonElement body, IReadOnlyDictionary<string, string> parameters)
    {
        var parser = new LogicAppExpression(expression, body, parameters);
        var value = parser.Value();
        return parser._position == parser._tokens.Count ? value : throw new FormatException($"unexpected '{parser._tokens[parser._position]}'");
    }

    private object? Value()
    {
        var token = _tokens[_position++];
        if (token.StartsWith('\''))
        {
            return token[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        if (char.IsDigit(token[0]) || token[0] == '-')
        {
            return double.Parse(token, CultureInfo.InvariantCulture);
        }

        Expect("(");
        var arguments = new List<object?>();
        while (!Accept(")"))
        {
            arguments.Add(Value());
            Accept(",");
        }

        var value = token switch
        {
            "and" => arguments.All(a => a is true),
            "equals" => Equals(Normal(arguments[0]), Normal(arguments[1])),
            "greaterOrEquals" => Number(arguments[0]) >= Number(arguments[1]),
            "float" => Number(arguments[0]),
            "isFloat" => Normal(arguments[0]) is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            "if" => arguments[0] is true ? arguments[1] : arguments[2],
            "parameters" => _parameters[(string)arguments[0]!],
            "triggerBody" => (object?)_body,
            _ => throw new FormatException($"unsupported function {token}"),
        };

        // ?['name'] steps into an object; a missing property is null.
        while (Accept("?") || Peek("["))
        {
            Expect("[");
            var name = (string)Value()!;
            Expect("]");
            value = value is JsonElement { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(name, out var child) ? child : null;
        }

        return value;
    }

    private static object? Normal(object? value) => value is JsonElement element ? element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString() : value;

    // float() of anything that isn't a number fails the action, as it does in Logic Apps.
    private static double Number(object? value) => Normal(value) switch
    {
        double number => number,
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        var other => throw new InvalidOperationException($"'{other}' isn't a number"),
    };

    private bool Peek(string token) => _position < _tokens.Count && _tokens[_position] == token;

    private bool Accept(string token)
    {
        if (Peek(token))
        {
            _position++;
            return true;
        }

        return false;
    }

    private void Expect(string token)
    {
        if (!Accept(token))
        {
            throw new FormatException($"expected '{token}'");
        }
    }

    [GeneratedRegex(@"'(?:[^']|'')*'|-?\d+(?:\.\d+)?|[A-Za-z]+|[()\[\],?]")]
    private static partial Regex Token();
}
