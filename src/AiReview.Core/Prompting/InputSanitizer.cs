using System.Globalization;
using System.Text;
using AiReview.Contracts.Input;

namespace AiReview.Core.Prompting;

/// <summary>Cleans untrusted extracted field text before it goes into a prompt (IN-2).</summary>
public sealed class InputSanitizer
{
    public const int DefaultMaxValueLength = 500;

    private readonly int _maxValueLength;

    // Creates a sanitizer that caps each field value at maxValueLength characters.
    public InputSanitizer(int maxValueLength = DefaultMaxValueLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxValueLength);
        _maxValueLength = maxValueLength;
    }

    // Returns a copy of the request with every field name and value cleaned; IDs are left as-is so citations still match.
    public ReviewRequest Sanitize(ReviewRequest request) => request with
    {
        Documents = request.Documents
            .Select(doc => doc with
            {
                Fields = doc.Fields
                    .Select(field => field with
                    {
                        Name = StripControlCharacters(field.Name),
                        Value = Truncate(StripControlCharacters(field.Value), _maxValueLength),
                    })
                    .ToList(),
            })
            .ToList(),
    };

    // Turns tabs and line breaks into spaces and removes all other control and invisible formatting characters.
    public static string StripControlCharacters(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\t' or '\n' or '\r')
            {
                builder.Append(' ');
            }
            else if (!char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    // Cuts text to at most maxLength characters without splitting a surrogate pair.
    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }
        var cut = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return text[..cut];
    }
}
