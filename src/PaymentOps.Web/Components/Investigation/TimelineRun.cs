namespace PaymentOps.Web.Components.Investigation;

/// <summary>Where the run behind a timeline is: still going, over, or cancelled before its last step finished.</summary>
public enum TimelineRun
{
    Running,
    Finished,
    Stopped,
}
