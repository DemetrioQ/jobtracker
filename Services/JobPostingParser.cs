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
        ["Fully Remote", "100% Remote", "Remote", "Hybrid", "On-site", "Onsite"];

    // US state codes (2-letter) including DC. Used to validate that what
    // looks like "City, ST" is really a place name and not e.g. "Angular, React".
    private static readonly HashSet<string> UsStates = new(StringComparer.Ordinal)
    {
        "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA","HI","ID","IL","IN","IA",
        "KS","KY","LA","ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ",
        "NM","NY","NC","ND","OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VT",
        "VA","WA","WV","WI","WY","DC",
    };

    private static readonly string[] CountryNames =
    {
        "United States", "USA", "Canada", "United Kingdom", "UK",
        "Germany", "France", "Spain", "Italy", "Netherlands", "Ireland",
        "Australia", "Mexico", "Brazil", "Argentina", "India", "Japan",
        "Singapore", "Switzerland", "Sweden", "Denmark", "Norway", "Finland",
        "Poland", "Portugal", "Belgium", "Austria",
    };

    // City + 2-letter state (validated below) or city + comma + country name.
    [GeneratedRegex(@"\b([A-Z][a-zA-Z\.'\-]+(?:\s+[A-Z][a-zA-Z\.'\-]+){0,3}),\s+([A-Z]{2})\b")]
    private static partial Regex CityStateUsRegex();

    [GeneratedRegex(@"^\s*Location[:\s]+(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex LocationLabelRegex();

    private static string? ExtractLocation(string text)
    {
        // Explicit "Location: …" label always wins.
        var labelled = LocationLabelRegex().Match(text);
        if (labelled.Success)
        {
            var v = labelled.Groups[1].Value.Trim().TrimEnd('.', ',', ';');
            if (v.Length is > 0 and < 80) return v;
        }

        // Restrict heuristic search to the first ~600 chars / 8 lines, where
        // geographic info typically appears in job posts. This avoids picking
        // up things like "frameworks/libraries such as Angular, React, or Vue.js"
        // from deep in a responsibilities list.
        var head = string.Join('\n', text.Split('\n').Take(8));
        if (head.Length > 600) head = head[..600];

        var cityState = FindValidCityState(head);
        var remote = FindRemoteHint(head);

        if (cityState is not null && remote is not null)
            return $"{cityState} ({remote})";
        if (cityState is not null) return cityState;
        if (remote is not null) return remote;

        // Country names alone, e.g. "United Kingdom" appearing standalone.
        foreach (var country in CountryNames)
        {
            var idx = head.IndexOf(country, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // Only accept if it's a standalone token, not part of a sentence
                // about the company being a "United States-based corporation".
                var before = idx == 0 ? ' ' : head[idx - 1];
                if (!char.IsLetter(before)) return country;
            }
        }

        return null;
    }

    private static string? FindValidCityState(string head)
    {
        foreach (Match m in CityStateUsRegex().Matches(head))
        {
            var state = m.Groups[2].Value;
            if (UsStates.Contains(state)) return m.Value;
        }
        return null;
    }

    private static string? FindRemoteHint(string head)
    {
        foreach (var hint in RemoteHints)
        {
            var idx = head.IndexOf(hint, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            // Token boundaries — don't match inside another word.
            var before = idx == 0 ? ' ' : head[idx - 1];
            var afterIdx = idx + hint.Length;
            var after = afterIdx >= head.Length ? ' ' : head[afterIdx];
            if (char.IsLetter(before) || char.IsLetter(after)) continue;
            return hint;
        }
        return null;
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

        // Fallback: prefer lines with title-affinity words (Engineer / Developer / …),
        // and only fall back to "first line that looks like a title" if none match.
        if (title is null)
        {
            foreach (var line in lines)
            {
                if (LooksLikeTitle(line) && HasTitleAffinity(line))
                {
                    title = Clean(line);
                    break;
                }
            }
        }

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

    // Common section headings that get pasted along with the job body but are
    // not actually the job title. These should never be returned as a Title.
    private static readonly string[] HeaderPhrases =
    {
        "about the job", "about the role", "about us", "about the company",
        "job description", "job summary", "summary", "overview",
        "the role", "your role", "the position", "position summary",
        "key responsibilities", "responsibilities", "duties",
        "what you'll do", "what you will do", "what we're looking for",
        "qualifications", "requirements", "preferred qualifications",
        "minimum qualifications", "skills", "experience", "compensation",
        "benefits", "perks", "why join us", "our team",
    };

    // Words commonly found in real job titles. A line containing one is more
    // likely to be the real title than a generic section header.
    private static readonly string[] TitleAffinity =
    {
        "Engineer", "Developer", "Architect", "Manager", "Lead", "Director",
        "Analyst", "Consultant", "Designer", "Specialist", "Officer", "Scientist",
        "Administrator", "Coordinator", "Strategist", "Researcher", "Programmer",
        "DevOps", "SRE", "Full Stack", "Frontend", "Backend", "Front-end", "Back-end",
        "Product", "Project", "Software", "Data", "Machine Learning", "ML",
    };

    private static bool LooksLikeTitle(string line)
    {
        if (line.Length is < 3 or > 120) return false;
        if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        if (line.Count(char.IsDigit) > line.Length / 3) return false;
        if (!line.Any(char.IsUpper) || !line.Any(char.IsLower)) return false;

        var lower = line.ToLowerInvariant().TrimEnd(':', '.', ' ', '–', '—', '-');
        foreach (var h in HeaderPhrases)
        {
            if (lower == h || lower.StartsWith(h + ":") || lower.StartsWith(h + " ")) return false;
        }

        // Reject lines that read like sentences (lots of common stop-words).
        var wordCount = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > 14) return false;

        return true;
    }

    private static bool HasTitleAffinity(string line)
    {
        foreach (var w in TitleAffinity)
        {
            if (line.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string Clean(string s)
    {
        var v = s.Trim().TrimEnd('.', ',', ';', ':', '|', '·', '—', '-', '–');
        return Regex.Replace(v, @"\s+", " ");
    }
}
