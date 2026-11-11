using System.Text.RegularExpressions;

namespace PaymentOps.Evaluation.Tests;

/// <summary>
/// Evaluates the subset of GitHub Actions expressions the evaluation workflow uses, so its conditions are tested as
/// written rather than by their text: context paths, string literals, ==, !=, !, &amp;&amp;, || and parentheses, always() and
/// contains(). A context path is looked up whole (labels.*.name included); one that's missing is the empty string.
/// </summary>
internal sealed partial class WorkflowExpression
{
    private readonly List<string> _tokens;
    private readonly IReadOnlyDictionary<string, object> _context;
    private int _position;

    private WorkflowExpression(string expression, IReadOnlyDictionary<string, object> context)
    {
        var body = expression.Trim();
        if (body.StartsWith("${{", StringComparison.Ordinal))
        {
            body = body[3..^2];
        }

        _tokens = [.. Token().Matches(body).Select(m => m.Value)];
        _context = context;
    }

    public static object Evaluate(string expression, IReadOnlyDictionary<string, object> context)
    {
        var parser = new WorkflowExpression(expression, context);
        var value = parser.Or();
        return parser._position == parser._tokens.Count
            ? value
            : throw new FormatException($"unexpected '{parser._tokens[parser._position]}' in {expression}");
    }

    public static bool IsTrue(string expression, IReadOnlyDictionary<string, object> context) => Truthy(Evaluate(expression, context));

    // Like GitHub, && and || return an operand rather than a bool, so "x && '1' || '3'" picks a value.
    private object Or()
    {
        var left = And();
        while (Accept("||"))
        {
            var right = And();
            left = Truthy(left) ? left : right;
        }

        return left;
    }

    private object And()
    {
        var left = Comparison();
        while (Accept("&&"))
        {
            var right = Comparison();
            left = Truthy(left) ? right : left;
        }

        return left;
    }

    private object Comparison()
    {
        var left = Unary();
        if (Accept("=="))
        {
            return Equal(left, Unary());
        }

        return Accept("!=") ? !Equal(left, Unary()) : left;
    }

    private object Unary() => Accept("!") ? !Truthy(Unary()) : Primary();

    private object Primary()
    {
        var token = _tokens[_position++];
        if (token == "(")
        {
            var value = Or();
            Expect(")");
            return value;
        }

        if (token.StartsWith('\''))
        {
            return token[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        if (Accept("("))
        {
            var arguments = new List<object>();
            while (!Accept(")"))
            {
                arguments.Add(Or());
                Accept(",");
            }

            return token switch
            {
                "always" => true,
                "contains" when arguments[0] is IEnumerable<string> items => items.Contains((string)arguments[1], StringComparer.OrdinalIgnoreCase),
                "contains" => Text(arguments[0]).Contains(Text(arguments[1]), StringComparison.OrdinalIgnoreCase),
                _ => throw new FormatException($"unsupported function {token}"),
            };
        }

        return token switch
        {
            "true" => true,
            "false" => false,
            _ => _context.TryGetValue(token, out var value) ? value : string.Empty,
        };
    }

    private bool Accept(string token)
    {
        if (_position < _tokens.Count && _tokens[_position] == token)
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

    private static bool Equal(object left, object right) => string.Equals(Text(left), Text(right), StringComparison.OrdinalIgnoreCase);

    private static string Text(object value) => value switch
    {
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? string.Empty,
    };

    private static bool Truthy(object value) => value switch
    {
        bool b => b,
        string s => s.Length > 0,
        _ => true,
    };

    [GeneratedRegex(@"'(?:[^']|'')*'|==|!=|&&|\|\||[!(),]|[A-Za-z_][\w.\-*]*")]
    private static partial Regex Token();
}
