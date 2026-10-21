using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentOps.Agent;

/// <summary>How model output is read: every field required, nothing unknown, nulls only where declared.</summary>
internal static class StrictJson
{
    public static JsonSerializerOptions Create(JsonNamingPolicy? naming = null) => new()
    {
        PropertyNamingPolicy = naming,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };
}
