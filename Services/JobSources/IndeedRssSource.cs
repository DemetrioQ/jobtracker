using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using jobtracker.Data;
using Microsoft.Extensions.Options;

namespace jobtracker.Services.JobSources;

public sealed class IndeedRssSource(
    IHttpClientFactory httpFactory,
    IOptions<JobIngestionOptions> options,
    ILogger<IndeedRssSource> logger) : IJobSource
{
    private readonly JobIngestionOptions.IndeedOptions _opts = options.Value.Indeed;

    public string Name => "indeed";

    public async IAsyncEnumerable<JobListing> FetchAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_opts.Queries is null || _opts.Queries.Length == 0)
            yield break;

        var http = httpFactory.CreateClient(nameof(IndeedRssSource));

        foreach (var query in _opts.Queries)
        {
            var locations = (_opts.Locations is { Length: > 0 })
                ? _opts.Locations
                : new[] { "" };

            foreach (var loc in locations)
            {
                var url = BuildUrl(query, loc);
                XDocument? doc = null;

                using (var resp = await SafeGetAsync(http, url, ct))
                {
                    if (resp is null) continue;

                    var contentType = resp.Content.Headers.ContentType?.MediaType ?? "(none)";
                    string body = "";
                    try { body = await resp.Content.ReadAsStringAsync(ct); }
                    catch (Exception ex) { logger.LogWarning(ex, "Indeed: read body failed for {Url}", url); }

                    logger.LogInformation(
                        "Indeed RSS {Url} -> status={Status} contentType={Ct} bytes={Len}",
                        url, (int)resp.StatusCode, contentType, body.Length);

                    if (!resp.IsSuccessStatusCode)
                    {
                        logger.LogWarning("Indeed RSS non-success head: {Head}", Head(body));
                        continue;
                    }

                    var trimmed = body.TrimStart();
                    if (string.IsNullOrWhiteSpace(trimmed) ||
                        (!trimmed.StartsWith("<?xml") && !trimmed.StartsWith("<rss")))
                    {
                        logger.LogWarning("Indeed RSS returned non-XML body — likely a bot/HTML challenge. Head: {Head}", Head(body));
                        continue;
                    }

                    try { doc = XDocument.Parse(body); }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Indeed RSS XML parse failed. Head: {Head}", Head(body));
                        continue;
                    }
                }

                if (doc is null) continue;

                var items = doc.Descendants("item").ToList();
                logger.LogInformation("Indeed RSS items={Count} for q='{Query}' l='{Loc}'", items.Count, query, loc);
                foreach (var item in items)
                {
                    var listing = TryParseItem(item, query, loc);
                    if (listing is not null) yield return listing;
                }
            }
        }
    }

    private string BuildUrl(string query, string location)
    {
        // fromage=1 → posted within the last day
        var q = WebUtility.UrlEncode(query);
        var l = WebUtility.UrlEncode(location);
        return $"https://www.indeed.com/rss?q={q}&l={l}&fromage=1&sort=date";
    }

    private JobListing? TryParseItem(XElement item, string query, string location)
    {
        var rawTitle = (string?)item.Element("title");
        var link = (string?)item.Element("link");
        var pubDate = (string?)item.Element("pubDate");
        var description = (string?)item.Element("description");
        var guid = (string?)item.Element("guid") ?? link;

        if (string.IsNullOrWhiteSpace(rawTitle) || string.IsNullOrWhiteSpace(link))
            return null;

        // Indeed RSS title format: "Job Title - Company - City, ST"
        var (title, company, parsedLoc) = SplitTitle(rawTitle);

        var posted = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(pubDate) &&
            DateTimeOffset.TryParse(pubDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            posted = dto.UtcDateTime;
        }

        var loc = parsedLoc ?? (string.IsNullOrWhiteSpace(location) ? null : location);
        var remote = (loc?.Contains("remote", StringComparison.OrdinalIgnoreCase) ?? false)
                     || (rawTitle.Contains("remote", StringComparison.OrdinalIgnoreCase));

        return new JobListing
        {
            Source = Name,
            ExternalId = guid!.Length > 200 ? guid[..200] : guid,
            Title = Clip(title, 300)!,
            Company = Clip(company ?? "", 200)!,
            Location = Clip(loc, 200),
            Remote = remote,
            Url = Clip(link, 800)!,
            PostedAt = posted,
            FetchedAt = DateTime.UtcNow,
            Description = Clip(StripHtml(description), 500),
            Tags = Clip(query, 300),
        };
    }

    private static (string Title, string? Company, string? Location) SplitTitle(string s)
    {
        // Walks " - " separators from the right since titles can contain " - "
        // (e.g. "Sr. Engineer - .NET - Acme - Austin, TX").
        var parts = s.Split(" - ", StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            >= 3 => (string.Join(" - ", parts[..^2]), parts[^2], parts[^1]),
            2    => (parts[0], parts[1], null),
            _    => (s, null, null),
        };
    }

    private static string? Clip(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length > max ? s[..max] : s);

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
    }

    private async Task<HttpResponseMessage?> SafeGetAsync(HttpClient http, string url, CancellationToken ct)
    {
        try { return await http.GetAsync(url, ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Indeed RSS fetch failed for {Url}", url);
            return null;
        }
    }

    private static string Head(string s, int n = 240)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "…");
}
