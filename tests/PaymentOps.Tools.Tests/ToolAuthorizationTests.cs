namespace PaymentOps.Tools.Tests;

public sealed class ToolAuthorizationTests
{
    public static TheoryData<string> ReadTools => [.. ToolNames.ReadTools];

    [Theory]
    [MemberData(nameof(ReadTools))]
    public void Every_read_tool_needs_an_ops_reader(string tool)
    {
        Assert.True(ToolAuthorization.Allows(TestUser.Reader, tool));
        Assert.False(ToolAuthorization.Allows(TestUser.General, tool));
        Assert.False(ToolAuthorization.Allows(TestUser.Anonymous with { Roles = TestUser.Reader.Roles }, tool));
    }

    [Fact]
    public void An_unknown_tool_is_never_allowed() =>
        Assert.False(ToolAuthorization.Allows(TestUser.Reader, "create_incident_ticket"));

    [Fact]
    public void Results_serialize_with_snake_case_names_and_codes() =>
        Assert.Equal("""{"status":"error","code":"range_too_large","message":"The range can be at most 31 days."}""",
            ToolJson.Serialize(new ToolResult.Failed(ToolErrorCode.RangeTooLarge, "The range can be at most 31 days.")));
}
