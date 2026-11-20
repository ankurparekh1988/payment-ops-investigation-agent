using System.Collections.Concurrent;

using Microsoft.Extensions.Options;

namespace PaymentOps.Web.Security;

/// <summary>One signed-in session as the server sees it.</summary>
internal sealed record SessionRecord(
    string Sid,
    string UserId,
    string TenantId,
    DateTimeOffset AbsoluteExpiry,
    DateTimeOffset LastActivity,
    DateTimeOffset? AuthenticatedAt,
    string AccessFingerprint);

internal sealed record SessionStatus(bool IsLive, TimeSpan IdleRemaining, DateTimeOffset? AuthenticatedAt)
{
    public static SessionStatus SignedOut { get; } = new(false, TimeSpan.Zero, null);
}

/// <summary>The server's own record of who is signed in and when they were last active; a cookie alone never keeps a session alive.</summary>
internal sealed class SessionRegistry(TimeProvider time, IOptions<AccessOptions> options)
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly TimeSpan _idleTimeout = options.Value.IdleTimeout;

    /// <summary>Adds a session and drops any past their absolute expiry. A duplicate sid throws.</summary>
    public void Add(SessionRecord record)
    {
        var now = time.GetUtcNow();
        foreach (var expired in _sessions.Where(s => now >= s.Value.AbsoluteExpiry))
        {
            _sessions.TryRemove(expired);
        }

        if (!_sessions.TryAdd(record.Sid, record))
        {
            throw new InvalidOperationException("A session with this id already exists.");
        }
    }

    public bool Remove(string sid) => _sessions.TryRemove(sid, out _);

    public SessionRecord? Find(string sid) => _sessions.GetValueOrDefault(sid);

    public SessionStatus Status(string? sid, string? userId)
    {
        if (sid is null || userId is null || !_sessions.TryGetValue(sid, out var record) || record.UserId != userId)
        {
            return SessionStatus.SignedOut;
        }

        var now = time.GetUtcNow();
        return IsLive(record, now)
            ? new SessionStatus(true, record.LastActivity + _idleTimeout - now, record.AuthenticatedAt)
            : SessionStatus.SignedOut;
    }

    /// <summary>Counts activity, but only for a session that is still live.</summary>
    public bool Touch(string sid, string userId) =>
        Update(sid, userId, (record, now) => record with { LastActivity = now > record.LastActivity ? now : record.LastActivity });

    /// <summary>Records a sign-in or step-up, keeping the later time so an older token cannot make a session look staler.</summary>
    public bool RecordAuthentication(string sid, string userId, DateTimeOffset authenticatedAt) =>
        Update(sid, userId, (record, _) => record with
        {
            AuthenticatedAt = record.AuthenticatedAt is { } existing && existing >= authenticatedAt ? existing : authenticatedAt,
        });

    // Compare-and-swap, never an upsert: a session removed mid-update must not come back.
    private bool Update(string sid, string userId, Func<SessionRecord, DateTimeOffset, SessionRecord> change)
    {
        while (_sessions.TryGetValue(sid, out var current))
        {
            var now = time.GetUtcNow();
            if (current.UserId != userId || !IsLive(current, now))
            {
                return false;
            }

            if (_sessions.TryUpdate(sid, change(current, now), current))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsLive(SessionRecord record, DateTimeOffset now) =>
        now < record.LastActivity + _idleTimeout && now < record.AbsoluteExpiry;
}
