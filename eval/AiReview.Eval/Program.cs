using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AiReview.Contracts.Output;
using AiReview.Core;
using AiReview.Core.Input;
using AiReview.Core.Services;
using AiReview.Eval;
using AiReview.Infrastructure;
using AiReview.Providers.Bedrock;
using AiReview.Providers.Deterministic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Runs every case in eval/cases through the full Bedrock flow (validate, retry, fallback) and through the
// deterministic reviewer, scores each stored review against cases/expected.json, checks the injection
// criteria, and writes the section 10.5 results table to eval/results as Markdown and CSV.
//
//   dotnet run --project eval/AiReview.Eval                  (Bedrock settings from src/AiReview.Api/appsettings.json)
//   dotnet run --project eval/AiReview.Eval -- --offline     (deterministic path only, no network)
//
// Exits with code 1 when an injection case fails its checks.

var repoRoot = FindRepoRoot();
var casesDir = Path.Combine(repoRoot, "eval", "cases");
var resultsDir = Path.Combine(repoRoot, "eval", "results");
var offline = args.Contains("--offline");
var runAt = DateTimeOffset.UtcNow;

var expectations = LoadExpectations(Path.Combine(casesDir, "expected.json"));
var caseFiles = Directory.GetFiles(casesDir, "*.request.json").OrderBy(CaseOrder).ToList();

using var bedrockHost = offline ? null : BuildHost(repoRoot, args, provider: "bedrock");
using var deterministicHost = BuildHost(repoRoot, args, provider: "deterministic");

var rows = new List<ResultRow>();
var records = new List<object>();
var injectionFailures = 0;

foreach (var file in caseFiles)
{
    var caseName = Path.GetFileName(file)[..^".request.json".Length];
    var json = File.ReadAllText(file);
    expectations.TryGetValue(caseName, out var expectation);

    if (string.IsNullOrWhiteSpace(json))
    {
        rows.Add(new ResultRow { Case = caseName, Path = "-", Notes = "Case file is empty: persona not defined yet (D-2)." });
        continue;
    }

    var read = ReviewRequestReader.Read(json);
    if (!read.IsValid)
    {
        rows.Add(new ResultRow { Case = caseName, Path = "-", Notes = "Invalid request: " + string.Join(" ", read.Errors) });
        continue;
    }

    var paths = new List<(string Name, IHost Host)>();
    if (bedrockHost is not null)
        paths.Add(("bedrock flow", bedrockHost));
    paths.Add(("deterministic", deterministicHost));

    foreach (var (pathName, host) in paths)
    {
        var service = host.Services.GetRequiredService<ReviewService>();
        var stopwatch = Stopwatch.StartNew();
        var record = await service.GenerateAsync(read.Request!, CancellationToken.None);
        stopwatch.Stop();

        records.Add(new { @case = caseName, path = pathName, latency_ms = stopwatch.ElapsedMilliseconds, record });
        var (row, injectionFailed) = BuildRow(caseName, pathName, expectation, record, read.Request!, stopwatch.Elapsed);
        rows.Add(row);
        if (injectionFailed)
            injectionFailures++;

        Console.WriteLine($"{caseName,-6} {pathName,-14} provider={record.Provider,-13} attempts={record.Attempts} fallback={record.FallbackReason ?? "-"} {stopwatch.Elapsed.TotalSeconds:0.0}s");
    }
}

var modelId = bedrockHost?.Services.GetRequiredService<IConfiguration>()["AiReview:Bedrock:ModelId"];
var header = $"# Evaluation results\n\nRun at {runAt:yyyy-MM-dd HH:mm:ss} UTC. " +
             (offline ? "Offline run: deterministic path only." : $"Model: `{modelId}`.") +
             " Prompt version: `prompt-v1`. Expected issues are from `eval/cases/expected.json`.";

var (markdownPath, csvPath) = ResultsWriter.Write(resultsDir, rows, header, runAt);
var recordsPath = Path.Combine(resultsDir, $"results-{runAt:yyyyMMdd-HHmmss}.records.json");
File.WriteAllText(recordsPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine();
Console.WriteLine(ResultsWriter.ToMarkdown(rows, header));
Console.WriteLine($"Wrote {markdownPath}");
Console.WriteLine($"Wrote {csvPath}");
Console.WriteLine($"Wrote {recordsPath} (full stored reviews, for hand-checking unsupported concerns)");

return injectionFailures > 0 ? 1 : 0;

// Builds the row for one stored review; returns whether an injection case failed its checks.
static (ResultRow Row, bool InjectionFailed) BuildRow(
    string caseName, string pathName, CaseExpectation? expectation, ReviewRecord record,
    AiReview.Contracts.Input.ReviewRequest request, TimeSpan latency)
{
    var notes = new List<string>();
    var row = new ResultRow
    {
        Case = caseName,
        Path = pathName,
        Provider = record.Provider,
        Attempts = record.Attempts.ToString(CultureInfo.InvariantCulture),
        FirstTryValid = YesNo(!record.IsFallback && record.Attempts == 1),
        FellBack = record.IsFallback ? $"Yes ({record.FallbackReason})" : "No",
        Confidence = string.Create(CultureInfo.InvariantCulture, $"{record.Confidence.Band} {record.Confidence.Score:0.00}"),
        Latency = string.Create(CultureInfo.InvariantCulture, $"{latency.TotalSeconds:0.0} s"),
    };

    if (expectation is null)
    {
        notes.Add("Expected issues not written in expected.json (D-2).");
        return (row with { Notes = string.Join(" ", notes) }, false);
    }

    var score = EvalScorer.Score(expectation, record);
    row = row with
    {
        Expected = string.Join(", ", score.Expected),
        Caught = $"{score.Caught.Count}/{score.Expected.Count}" + (score.Caught.Count > 0 ? $" ({string.Join(", ", score.Caught)})" : ""),
        Missed = score.Missed.Count == 0 ? "-" : string.Join(", ", score.Missed),
        Unsupported = score.Unsupported.ToString(CultureInfo.InvariantCulture),
    };
    if (score.Unsupported > 0)
        notes.Add($"{score.Unsupported} concern(s) cite no expected finding: check by hand.");

    var injectionFailed = false;
    if (expectation.Injection is not null)
    {
        var check = InjectionChecks.Evaluate(record, request);
        injectionFailed = !check.Pass;
        row = row with { Expected = row.Expected + "; injection flagged; no approval" };
        notes.Add(
            $"INJ {(check.Pass ? "PASS" : "FAIL")}: " +
            $"valid {YesNo(check.PassesValidation)}, " +
            $"no approval {(check.NoApprovalLanguage ? "Yes" : $"No (\"{check.ApprovalPhrase}\")")}, " +
            $"flagged {(check.Flagged ? $"Yes ({check.FlaggedIn})" : "No (not acted on)")}, " +
            "case state unchanged Yes (no case-state access, APR-5).");
    }

    return (row with { Notes = string.Join(" ", notes) }, injectionFailed);
}

static string YesNo(bool value) => value ? "Yes" : "No";

// Loads src/AiReview.Api/appsettings.json (so the eval uses the API's model settings), then environment
// variables and command-line overrides, and forces the given provider.
static IHost BuildHost(string repoRoot, string[] args, string provider)
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args.Where(a => a != "--offline").ToArray() });
    builder.Configuration.Sources.Clear();
    builder.Configuration
        .AddJsonFile(Path.Combine(repoRoot, "src", "AiReview.Api", "appsettings.json"), optional: false)
        .AddEnvironmentVariables()
        .AddCommandLine(args.Where(a => a != "--offline").ToArray())
        .AddInMemoryCollection(new Dictionary<string, string?> { ["AiReview:Provider"] = provider });

    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning);

    builder.Services
        .AddReviewCore(builder.Configuration)
        .AddBedrockReviewer(builder.Configuration)
        .AddDeterministicReviewer()
        .AddInMemoryStores();

    return builder.Build();
}

static Dictionary<string, CaseExpectation> LoadExpectations(string path)
{
    var text = File.Exists(path) ? File.ReadAllText(path) : "";
    return string.IsNullOrWhiteSpace(text)
        ? new Dictionary<string, CaseExpectation>()
        : JsonSerializer.Deserialize<Dictionary<string, CaseExpectation>>(text) ?? new();
}

// P1..P5 first, then everything else (INJ-1) by name.
static string CaseOrder(string file)
{
    var name = Path.GetFileName(file);
    return (name.StartsWith('P') ? "0" : "1") + name;
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "AiReviewAgent.sln")))
            return dir.FullName;
    }
    throw new InvalidOperationException("Could not find AiReviewAgent.sln above the eval output directory.");
}
