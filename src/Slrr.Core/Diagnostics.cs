namespace Slrr.Core;

public enum Severity
{
    Info,
    Warning,
    Error,
}

/// <summary>One finding from a parser or validator, located by file and (1-based) line when known.</summary>
public sealed record Diagnostic(Severity Severity, string Code, string Message, string? File = null, int Line = 0)
{
    public override string ToString()
    {
        var loc = File is null ? string.Empty : Line > 0 ? $"{File}({Line}): " : $"{File}: ";
        return $"{loc}{Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
    }
}
