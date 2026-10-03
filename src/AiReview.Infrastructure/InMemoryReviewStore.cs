using AiReview.Contracts.Output;
using AiReview.Core.Persistence;

namespace AiReview.Infrastructure;

/// <summary>Process-local review store for demos, tests, and the evaluation harness. Contents are lost on restart.</summary>
public sealed class InMemoryReviewStore : IReviewStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<ReviewRecord>> _byCase = new(StringComparer.Ordinal);

    public Task<ReviewRecord> AppendAsync(ReviewRecord record, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_byCase.TryGetValue(record.CaseId, out var versions))
            {
                versions = new List<ReviewRecord>();
                _byCase[record.CaseId] = versions;
            }
            var stored = record with { Version = versions.Count + 1 };
            versions.Add(stored);
            return Task.FromResult(stored);
        }
    }

    public Task<ReviewRecord?> GetLatestAsync(string caseId, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult(_byCase.TryGetValue(caseId, out var versions) ? versions[^1] : null);
        }
    }

    public Task<ReviewRecord?> GetVersionAsync(string caseId, int version, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult(Find(caseId, version));
        }
    }

    public Task<IReadOnlyList<ReviewRecord>> ListAsync(string caseId, CancellationToken ct)
    {
        lock (_lock)
        {
            IReadOnlyList<ReviewRecord> list = _byCase.TryGetValue(caseId, out var versions)
                ? versions.ToList()
                : Array.Empty<ReviewRecord>();
            return Task.FromResult(list);
        }
    }

    public Task<ReviewRecord?> SetAnalystCaseNoteAsync(string caseId, int version, string? note, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_byCase.TryGetValue(caseId, out var versions) || versions.Count != version)
            {
                return Task.FromResult<ReviewRecord?>(null);
            }
            var updated = versions[^1] with { AnalystCaseNote = note };
            versions[^1] = updated;
            return Task.FromResult<ReviewRecord?>(updated);
        }
    }

    // Versions start at 1 and are dense, so version n is at index n - 1.
    private ReviewRecord? Find(string caseId, int version) =>
        _byCase.TryGetValue(caseId, out var versions) && version >= 1 && version <= versions.Count
            ? versions[version - 1]
            : null;
}
