using System.Reflection;
using AiReview.Contracts.Output;
using AiReview.Core;
using AiReview.Infrastructure;
using AiReview.Providers.Bedrock;
using AiReview.Providers.Deterministic;

namespace AiReview.Architecture.Tests;

/// <summary>
/// APR-5: the review module (reviewers, ReviewService, ApprovalService, stores) cannot call case
/// state-transition code. It is enforced by references: the module may depend only on itself, the .NET
/// runtime, Microsoft.Extensions, and the AWS SDK, so a reference to any case or workflow assembly fails here.
/// </summary>
public class DependencyRuleTests
{
    private static readonly Assembly[] ReviewModule =
    {
        typeof(ReviewOutput).Assembly,          // AiReview.Contracts
        typeof(IAiReviewer).Assembly,           // AiReview.Core
        typeof(BedrockReviewer).Assembly,       // AiReview.Providers.Bedrock
        typeof(DeterministicReviewer).Assembly, // AiReview.Providers.Deterministic
        typeof(InMemoryReviewStore).Assembly,   // AiReview.Infrastructure
    };

    private static readonly string[] AllowedPrefixes = { "System", "Microsoft.Extensions.", "Microsoft.Win32.", "AWSSDK.", "netstandard", "mscorlib" };

    private static readonly string[] ForbiddenNameParts = { "CaseState", "CaseWorkflow", "CaseStatus", "StateTransition", "Workflow" };

    public static TheoryData<string> ModuleAssemblies()
    {
        var data = new TheoryData<string>();
        foreach (var assembly in ReviewModule)
            data.Add(assembly.GetName().Name!);
        return data;
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void ReviewModule_ReferencesOnlyAllowedAssemblies(string assemblyName)
    {
        var moduleNames = ReviewModule.Select(a => a.GetName().Name!).ToHashSet();

        var disallowed = Load(assemblyName).GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(name => !moduleNames.Contains(name) && !AllowedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(disallowed.Count == 0, $"{assemblyName} references assemblies outside the review module: {string.Join(", ", disallowed)}");
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void ReviewModule_HasNoCaseStateTypesOrMembers(string assemblyName)
    {
        var offenders = Load(assemblyName).GetTypes()
            .SelectMany(type => new[] { type.FullName ?? type.Name }
                .Concat(type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(member => $"{type.FullName}.{member.Name}")))
            .Where(name => ForbiddenNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(offenders.Count == 0, $"{assemblyName} has case-state names: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Core_DoesNotDependOnProvidersInfrastructureOrApi()
    {
        var references = typeof(IAiReviewer).Assembly.GetReferencedAssemblies().Select(r => r.Name!).ToList();

        Assert.DoesNotContain(references, name =>
            name.StartsWith("AiReview.Providers", StringComparison.Ordinal)
            || name is "AiReview.Infrastructure" or "AiReview.Api");
    }

    [Fact]
    public void Providers_DependOnlyOnCoreAndContracts()
    {
        foreach (var provider in new[] { typeof(BedrockReviewer).Assembly, typeof(DeterministicReviewer).Assembly })
        {
            var internalRefs = provider.GetReferencedAssemblies().Select(r => r.Name!).Where(n => n.StartsWith("AiReview.", StringComparison.Ordinal));
            Assert.All(internalRefs, name => Assert.Contains(name, new[] { "AiReview.Core", "AiReview.Contracts" }));
        }
    }

    [Fact]
    public void ForbiddenNameCheck_CatchesACaseStateType()
    {
        // Guards the guard: the name check must flag a type like the one APR-5 forbids.
        Assert.Contains(ForbiddenNameParts, part => nameof(ICaseStateService).Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private static Assembly Load(string name) => ReviewModule.Single(a => a.GetName().Name == name);

    private interface ICaseStateService;
}
