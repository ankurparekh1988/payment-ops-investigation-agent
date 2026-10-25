using Azure;
using Azure.Identity;

namespace PaymentOps.Knowledge.Indexing;

/// <summary>
/// The knowledge publisher's exit codes, so the evaluation workflow can tell its failures apart:
/// 0 published, 1 corpus, release or publish failure, 3 sign-in or Search unavailable, 4 scope violation.
/// </summary>
public static class PublishExitCode
{
    public const int Published = 0;
    public const int Failed = 1;
    public const int Unavailable = 3;
    public const int ScopeViolation = 4;

    public static int For(Exception? error) => error switch
    {
        null => Published,
        PublisherScopeException => ScopeViolation,
        AuthenticationFailedException or HttpRequestException => Unavailable,

        // A missing role or a missing index means the environment isn't set up, not that the change is wrong.
        KnowledgePublishException { InnerException: RequestFailedException { Status: 0 or 401 or 403 or 404 or 429 or >= 500 } } => Unavailable,
        KnowledgePublishException { InnerException: HttpRequestException } => Unavailable,
        _ => Failed,
    };
}
