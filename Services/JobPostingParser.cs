using System.Text.RegularExpressions;

namespace jobtracker.Services;

public sealed record ExtractedJob(
    string? Title,
    string? Company,
    string? Location,
    string? Salary,
    string? Url);

/// <summary>
/// Best-effort heuristic extraction of job-posting fields from pasted text.
/// Tuned to common copy-pasted patterns from LinkedIn / Indeed / Wellfound /
/// company career pages. Output is meant to prefill the quick-add form for
/// the user to review, not to be authoritative.
/// </summary>
public static partial class JobPostingParser
{
    public static ExtractedJob Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new(null, null, null, null, null);

        var t = NormalizeWhitespace(text);

        var url = ExtractUrl(t);
        var salary = ExtractSalary(t);
        var location = ExtractLocation(t);
        var (title, company) = ExtractTitleAndCompany(t);

        return new(title, company, location, salary, url);
    }

    private static string NormalizeWhitespace(string s)
        => s.Replace("\r\n", "\n").Replace('\r', '\n');

    // ── URL ──────────────────────────────────────────────────────

    [GeneratedRegex(@"https?://[^\s)<>""'\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    private static string? ExtractUrl(string text)
    {
        var m = UrlRegex().Match(text);
        return m.Success ? m.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}') : null;
    }

    // ── Salary ───────────────────────────────────────────────────

    // Matches things like:
    //   $120,000
    //   $120k
    //   $120K - $150K
    //   $120,000 – $150,000 / year
    //   $50/hour
    //   USD 120,000
    [GeneratedRegex(
        @"(?ix)
          (?:\$|USD\s*|US\$|CAD\s*|EUR\s*|€|£|GBP\s*)
          \s*\d[\d,\.]*\s*[kKmM]?
          (?:\s*(?:-|–|—|to)\s*(?:\$|USD\s*|€|£)?\s*\d[\d,\.]*\s*[kKmM]?)?
          (?:\s*(?:/|per)\s*(?:year|yr|annum|hour|hr|month|mo))?",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex SalaryRegex();

    private static string? ExtractSalary(string text)
    {
        var m = SalaryRegex().Match(text);
        if (!m.Success) return null;
        var value = m.Value.Trim();
        // Reject obvious noise: tiny numbers like "$5" likely aren't salary.
        if (Regex.IsMatch(value, @"^\$?\d{1,2}(\.\d+)?[^kKmM]?$"))
            return null;
        return value;
    }

    // ── Location ─────────────────────────────────────────────────

    private static readonly string[] RemoteHints =
        ["Remote", "Fully Remote", "100% Remote", "Hybrid", "On-site", "Onsite"];

    [GeneratedRegex(
        @"\b([A-Z][a-zA-Z\.\-]+(?:\s[A-Z][a-zA-Z\.\-]+)*),\s*([A-Z]{2}|[A-Z][a-z]+(?:\s[A-Z][a-z]+)?)\b")]
    private static partial Regex CityStateRegex();

    [GeneratedRegex(@"^\s*Location[:\s]+(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex LocationLabelRegex();

    private static string? ExtractLocation(string text)
    {
        var labelled = LocationLabelRegex().Match(text);
        if (labelled.Success)
        {
            var v = labelled.Groups[1].Value.Trim().TrimEnd('.', ',', ';');
            if (v.Length is > 0 and < 80) return v;
        }

        var firstLines = string.Join('\n', text.Split('\n').Take(15));

        foreach (var hint in RemoteHints)
        {
            var idx = firstLines.IndexOf(hint, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var cityState = CityStateRegex().Match(firstLines);
                if (cityState.Success)
                    return $"{cityState.Value} ({hint})";
                return hint;
            }
        }

        var cs = CityStateRegex().Match(firstLines);
        return cs.Success ? cs.Value : null;
    }

    // ── Title + Company ─────────────────────────────────────────

    [GeneratedRegex(@"\b(?:at|@|\|)\s+([A-Z][\w&'\.\- ]{1,60}?)(?:\s*(?:\||•|·|—|-|–)|\s*$|\s+is\b|\s+hiring\b|\.|\,)",
        RegexOptions.IgnoreCase)]
    private static partial Regex AtCompanyRegex();

    [GeneratedRegex(@"^\s*(?:Company|Employer)[:\s]+(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex CompanyLabelRegex();

    [GeneratedRegex(@"^\s*(?:Title|Position|Role|Job\s+Title)[:\s]+(.+?)\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex TitleLabelRegex();

    private static (string? Title, string? Company) ExtractTitleAndCompany(string text)
    {
        string? title = null, company = null;

        var titleLabel = TitleLabelRegex().Match(text);
        if (titleLabel.Success) title = Clean(titleLabel.Groups[1].Value);

        var companyLabel = CompanyLabelRegex().Match(text);
        if (companyLabel.Success) company = Clean(companyLabel.Groups[1].Value);

        if (title is not null && company is not null) return (title, company);

        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(20)
            .ToList();

        // Pattern: "<Title> at <Company>"
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"^(.{3,120}?)\s+(?:at|@)\s+([A-Z][\w&'\.\- ]{1,60})\s*$",
                RegexOptions.IgnoreCase);
            if (m.Success)
            {
                title ??= Clean(m.Groups[1].Value);
                company ??= Clean(m.Groups[2].Value);
                break;
            }
        }

        // Pattern: "<Company> hiring <Title>" (LinkedIn og:title)
        if (title is null || company is null)
        {
            foreach (var line in lines)
            {
                var m = Regex.Match(line, @"^([A-Z][\w&'\.\- ]{1,60})\s+(?:is\s+)?hiring\s+(.{3,120})$",
                    RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    company ??= Clean(m.Groups[1].Value);
                    title ??= Clean(m.Groups[2].Value);
                    break;
                }
            }
        }

        if (company is null)
        {
            var atMatch = AtCompanyRegex().Match(text);
            if (atMatch.Success) company = Clean(atMatch.Groups[1].Value);
        }

        // Fallback: if no title found, take the first line that "looks like" a title.
        if (title is null)
        {
            foreach (var line in lines)
            {
                if (LooksLikeTitle(line))
                {
                    title = Clean(line);
                    break;
                }
            }
        }

        return (title, company);
    }

    private static bool LooksLikeTitle(string line)
    {
        if (line.Length is < 3 or > 120) return false;
        if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        if (line.Count(char.IsDigit) > line.Length / 3) return false; // mostly numbers? no
        // Should contain at least one capital and at least one lowercase letter
        if (!line.Any(char.IsUpper) || !line.Any(char.IsLower)) return false;
        return true;
    }

    private static string Clean(string s)
    {
        var v = s.Trim().TrimEnd('.', ',', ';', ':', '|', '·', '—', '-', '–');
        return Regex.Replace(v, @"\s+", " ");
    }
}
