using AiReview.Contracts.Approval;
using AiReview.Core.Persistence;

namespace AiReview.Infrastructure;

/// <summary>Process-local approval store for demos, tests, and the evaluation harness. Contents are lost on restart.</summary>
public sealed class InMemoryApprovalStore : IApprovalStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<ApprovalRecord>> _byCase = new(StringComparer.Ordinal);

    public Task AddAsync(ApprovalRecord approval, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_byCase.TryGetValue(approval.CaseId, out var approvals))
            {
                approvals = new List<ApprovalRecord>();
                _byCase[approval.CaseId] = approvals;
            }
            approvals.Add(approval);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApprovalRecord>> ListAsync(string caseId, CancellationToken ct)
    {
        lock (_lock)
        {
            IReadOnlyList<ApprovalRecord> list = _byCase.TryGetValue(caseId, out var approvals)
                ? approvals.ToList()
                : Array.Empty<ApprovalRecord>();
            return Task.FromResult(list);
        }
    }
}
