using System.Reflection;

using PaymentOps.Domain.Merchants;

namespace PaymentOps.Tools.Tests;

public sealed class ToolContractRulesTests
{
    // Tool output is a contract with the model, separate from the ports: a Domain change mustn't change it silently.
    [Fact]
    public void No_public_tool_type_exposes_a_domain_type()
    {
        var domain = typeof(Merchant).Assembly;
        var offending = typeof(ToolResult).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => (type, property)))
            .Where(p => Flatten(p.property.PropertyType).Any(t => t.Assembly == domain))
            .Select(p => $"{p.type.Name}.{p.property.Name}");

        Assert.Empty(offending);
    }

    private static IEnumerable<Type> Flatten(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().SelectMany(Flatten).Prepend(type)
        : type.IsArray ? Flatten(type.GetElementType()!).Prepend(type)
        : [Nullable.GetUnderlyingType(type) ?? type];
}
