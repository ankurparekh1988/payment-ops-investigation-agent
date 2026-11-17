namespace PaymentOps.Web;

/// <summary>Deployment-specific text and links the shell shows; each is hidden when unset.</summary>
public sealed class UiOptions
{
    public const string SectionName = "PaymentOps:Ui";

    /// <summary>A badge naming the environment, such as "Demo · synthetic data".</summary>
    public string? EnvironmentLabel { get; set; }

    /// <summary>The operations workbook, linked from the Admin's operations panel.</summary>
    public Uri? WorkbookUrl { get; set; }

    /// <summary>The alert rules, linked from the Admin's operations panel.</summary>
    public Uri? AlertsUrl { get; set; }
}
