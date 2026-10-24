using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.AI;

using PaymentOps.Agent.Contracts;
using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Security;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Evaluation.Running;

/// <summary>
/// Everything one case's investigation did, observed from outside the app: model requests and tool calls, the
/// sections Search returned, the typed data behind each evidence id, the tickets created, and any failure of the
/// environment, including one the app absorbed into an ordinary result.
/// </summary>
internal sealed class CaseRecorder
{
    // The judge reads one record or fact, not a whole tool result; a bound keeps one large list from crowding the rest.
    private const int MaxFactChars = 4000;

    private readonly Lock _lock = new();
    private readonly List<ToolCallRecord> _calls = [];
    private readonly HashSet<string> _callIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RetrievedSection> _retrieved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _facts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _tickets = new(StringComparer.Ordinal);
    private int _requests;
    private long _inputTokens;
    private long _cachedInputTokens;
    private long _outputTokens;
    private string? _failure;

    public int Tickets
    {
        get
        {
            lock (_lock)
            {
                return _tickets.Count;
            }
        }
    }

    public IChatClient Observe(IChatClient model) => new RecordingChatClient(model, this);

    public IKnowledgeLookup Observe(IKnowledgeLookup knowledge) => new RecordingKnowledgeLookup(knowledge, this);

    public IInvestigationTool Observe(IInvestigationTool tool) => new RecordingTool(tool, this);

    public IIncidentStore Observe(IIncidentStore store) => new CountingIncidentStore(store, this);

    public void Fail(string failure)
    {
        lock (_lock)
        {
            _failure ??= failure;
        }
    }

    public CaseResult ToResult(string caseId, AssessmentResult result, string proposalState, long latencyMs, long? ttfpMs)
    {
        ArgumentNullException.ThrowIfNull(result);

        var assessed = result as AssessmentResult.Assessed;
        IReadOnlyList<Statement> statements = assessed is null ? []
            : [assessed.Summary, .. assessed.Claims, .. assessed.RootCause is { } root ? [root.Statement] : Array.Empty<Statement>(), .. assessed.NextSteps];
        var evidence = statements.SelectMany(s => s.Evidence).Select(e => e.Id).Distinct(StringComparer.Ordinal).ToList();
        var sections = statements.SelectMany(s => s.Citations).Select(c => c.SectionKey).Distinct(StringComparer.Ordinal).ToList();
        var proposal = assessed?.Proposal;
        var assessmentText = string.Join('\n', statements.Select(s => s.Text).Where(t => t.Length > 0));

        lock (_lock)
        {
            return new CaseResult(caseId, Outcomes.Of(result))
            {
                Calls = [.. _calls],
                AssessmentEvidenceIds = evidence,
                AssessmentSectionKeys = sections,
                ProposalEvidenceIds = [.. proposal?.Evidence.Select(e => e.Id) ?? []],
                ProposalSectionKeys = [.. proposal?.Citations.Select(c => c.SectionKey) ?? []],
                RetrievedSections = [.. _retrieved.Values.Select(s => new SectionHit(s.Key, s.Classification))],
                ProposalState = proposalState,
                InfrastructureFailure = _failure ?? result switch
                {
                    AssessmentResult.NotAssessed { Reason: NotAssessedReason.ModelUnavailable } => InfrastructureFailures.ModelUnavailable,
                    AssessmentResult.NotAssessed { Reason: NotAssessedReason.Busy } => InfrastructureFailures.ModelBusy,
                    _ => null,
                },
                LatencyMs = latencyMs,
                TtfpMs = ttfpMs,
                ModelRequests = _requests,
                InputTokens = _inputTokens,
                CachedInputTokens = _cachedInputTokens,
                OutputTokens = _outputTokens,
                AssessmentText = assessmentText,
                UserVisibleText = proposal is null ? assessmentText : $"{assessmentText}\n{proposal.Title}\n{proposal.Summary}",
                CitedEvidenceFacts = evidence.Where(_facts.ContainsKey).ToDictionary(id => id, id => _facts[id], StringComparer.Ordinal),
                CitedSectionText = sections.Where(_retrieved.ContainsKey).ToDictionary(key => key, key => _retrieved[key].Content, StringComparer.Ordinal),
            };
        }
    }

    // Calls the model made, and calls it was shown that it didn't make (a fixed workflow's), each once by call id.
    private void Exchange(IEnumerable<ChatMessage> sent, ChatResponse response)
    {
        lock (_lock)
        {
            _requests++;
            _inputTokens += response.Usage?.InputTokenCount ?? 0;
            _cachedInputTokens += response.Usage?.CachedInputTokenCount ?? 0;
            _outputTokens += response.Usage?.OutputTokenCount ?? 0;
            foreach (var call in sent.Concat(response.Messages).SelectMany(m => m.Contents).OfType<FunctionCallContent>().Where(call => _callIds.Add(call.CallId)))
            {
                _calls.Add(Record(call));
            }
        }
    }

    // Ids and dates only: a submission's and a knowledge query's text are the model's words and stay out of the record.
    private static ToolCallRecord Record(FunctionCallContent call) =>
        new(call.Name, call.Name is ToolNames.SubmitAssessment or ToolNames.KnowledgeSearch || call.Arguments is null
            ? new Dictionary<string, string?>()
            : call.Arguments.ToDictionary(a => a.Key, a => Text(a.Value), StringComparer.Ordinal));

    private static string? Text(object? value) => value switch
    {
        null => null,
        JsonElement { ValueKind: JsonValueKind.String } s => s.GetString(),
        JsonElement { ValueKind: JsonValueKind.Null } => null,
        JsonElement other => other.GetRawText(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private void Retrieved(IReadOnlyList<RetrievedSection> sections)
    {
        lock (_lock)
        {
            foreach (var section in sections)
            {
                _retrieved.TryAdd(section.Key, section);
            }
        }
    }

    // Each evidence item with the records in the tool's typed result that carry its id: server data, never model text.
    private void Returned(string tool, ToolResult.Ok ok)
    {
        lock (_lock)
        {
            var added = ok.Evidence.Where(item => !_facts.ContainsKey(item.Id)).ToList();
            var body = added.Count == 0 ? null : JsonSerializer.SerializeToNode(ok.Body, ok.Body.GetType(), ToolJson.Options);
            foreach (var item in added)
            {
                var fact = new JsonObject
                {
                    ["evidence"] = JsonSerializer.SerializeToNode(item, item.GetType(), ToolJson.Options),
                    ["returned_by"] = tool,
                    ["records"] = new JsonArray([.. RecordsFor(body, item.Id).Select(r => r.DeepClone())]),
                }.ToJsonString(ToolJson.Options);
                _facts[item.Id] = fact.Length <= MaxFactChars ? fact : fact[..MaxFactChars] + " [truncated]";
            }
        }
    }

    // Objects naming the id as their own evidence_id; a bare reference ({"evidence_id": ...}) adds nothing to the item.
    private static IEnumerable<JsonObject> RecordsFor(JsonNode? node, string id) => node switch
    {
        JsonObject o when o.Count > 1 && o["evidence_id"]?.GetValueKind() == JsonValueKind.String && (string?)o["evidence_id"] == id => [o],
        JsonObject o => o.SelectMany(p => RecordsFor(p.Value, id)),
        JsonArray a => a.SelectMany(child => RecordsFor(child, id)),
        _ => [],
    };

    private void Created(string ticketId)
    {
        lock (_lock)
        {
            _tickets.Add(ticketId);
        }
    }

    private sealed class RecordingChatClient(IChatClient inner, CaseRecorder recorder) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var sent = messages.ToList();
            var response = await base.GetResponseAsync(sent, options, cancellationToken);
            recorder.Exchange(sent, response);
            return response;
        }
    }

    // Records what Search returned, and notes when Search itself failed even though the tool turns that into a gap.
    private sealed class RecordingKnowledgeLookup(IKnowledgeLookup inner, CaseRecorder recorder) : IKnowledgeLookup
    {
        public async Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(string question, KnowledgeAccess access, CancellationToken cancellationToken)
        {
            try
            {
                var sections = await inner.RetrieveAsync(question, access, cancellationToken);
                recorder.Retrieved(sections);
                return sections;
            }
            catch (Exception ex) when (InfrastructureFailures.Of(ex) is { } failure)
            {
                // Only Search itself failing: a cancellation from the turn's own limits, or a bug, is the system's result.
                recorder.Fail(failure);
                throw;
            }
        }

        public Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken) =>
            inner.FindAsync(sectionKey, access, cancellationToken);
    }

    private sealed class RecordingTool(IInvestigationTool inner, CaseRecorder recorder) : IInvestigationTool
    {
        public string Name => inner.Name;

        public string Description => inner.Description;

        public Type RequestType => inner.RequestType;

        public string Source => inner.Source;

        public async Task<ToolResult> RunAsync(object request, IUserContext user, CancellationToken cancellationToken)
        {
            var result = await inner.RunAsync(request, user, cancellationToken);
            if (result is ToolResult.Ok ok)
            {
                recorder.Returned(inner.Name, ok);
            }

            return result;
        }
    }

    private sealed class CountingIncidentStore(IIncidentStore inner, CaseRecorder recorder) : IIncidentStore
    {
        public async Task<IncidentTicket> CreateAsync(string idempotencyKey, IncidentTicketContent content, CancellationToken cancellationToken)
        {
            var ticket = await inner.CreateAsync(idempotencyKey, content, cancellationToken);
            recorder.Created(ticket.Id);
            return ticket;
        }

        public Task<IncidentTicket?> FindAsync(string ticketId, CancellationToken cancellationToken) => inner.FindAsync(ticketId, cancellationToken);
    }
}
