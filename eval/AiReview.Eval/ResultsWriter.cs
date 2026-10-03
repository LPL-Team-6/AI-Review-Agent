using System.Globalization;
using System.Text;

namespace AiReview.Eval;

/// <summary>One row of the section 10.5 results table.</summary>
public sealed record ResultRow
{
    public required string Case { get; init; }
    public required string Path { get; init; }
    public string Expected { get; init; } = "";
    public string Caught { get; init; } = "";
    public string Missed { get; init; } = "";
    public string Unsupported { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Attempts { get; init; } = "";
    public string FirstTryValid { get; init; } = "";
    public string FellBack { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string Latency { get; init; } = "";
    public string Notes { get; init; } = "";
}

/// <summary>Writes the results table as Markdown and CSV, generated from the run rather than filled in by hand.</summary>
public static class ResultsWriter
{
    private static readonly string[] Headers =
    {
        "Case", "Path", "Expected", "Caught", "Missed", "Unsupported", "Provider", "Attempts",
        "1st-try valid", "Fell back", "Confidence", "Latency", "Notes",
    };

    // Writes results-{stamp}.md and results-{stamp}.csv to the directory and returns their paths.
    public static (string Markdown, string Csv) Write(string directory, IReadOnlyList<ResultRow> rows, string header, DateTimeOffset runAt)
    {
        Directory.CreateDirectory(directory);
        var stamp = runAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var markdownPath = System.IO.Path.Combine(directory, $"results-{stamp}.md");
        var csvPath = System.IO.Path.Combine(directory, $"results-{stamp}.csv");

        File.WriteAllText(markdownPath, ToMarkdown(rows, header));
        File.WriteAllText(csvPath, ToCsv(rows));
        return (markdownPath, csvPath);
    }

    public static string ToMarkdown(IReadOnlyList<ResultRow> rows, string header)
    {
        var builder = new StringBuilder();
        builder.AppendLine(header).AppendLine();
        builder.AppendLine("| " + string.Join(" | ", Headers) + " |");
        builder.AppendLine("|" + string.Concat(Headers.Select(_ => "---|")));
        foreach (var row in rows)
        {
            builder.AppendLine("| " + string.Join(" | ", Cells(row).Select(EscapeMarkdown)) + " |");
        }
        return builder.ToString();
    }

    public static string ToCsv(IReadOnlyList<ResultRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", Cells(row).Select(EscapeCsv)));
        }
        return builder.ToString();
    }

    private static IEnumerable<string> Cells(ResultRow row) => new[]
    {
        row.Case, row.Path, row.Expected, row.Caught, row.Missed, row.Unsupported, row.Provider, row.Attempts,
        row.FirstTryValid, row.FellBack, row.Confidence, row.Latency, row.Notes,
    };

    private static string EscapeMarkdown(string cell) => cell.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");

    private static string EscapeCsv(string cell) =>
        cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;
}
