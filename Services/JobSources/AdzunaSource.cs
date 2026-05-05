using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using jobtracker.Data;
using Microsoft.Extensions.Options;

namespace jobtracker.Services.JobSources;

/// <summary>
/// Adzuna jobs aggregator. Free tier with attribution. Skips itself silently
/// when AppId/AppKey aren't configured, so deployments without credentials
/// just don't pull from this source.
/// </summary>
public sealed class AdzunaSource(
    IHttpClientFactory httpFactory,
    IOptions<JobIngestionOptions> options,
    ILogger<AdzunaSource> logger) : IJobSource
{
    private readonly JobIngestionOptions.AdzunaOptions _opts = options.Value.Adzuna;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string Name => "adzuna";

    public async IAsyncEnumerable<JobListing> FetchAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.AppId) || string.IsNullOrWhiteSpace(_opts.AppKey))
        {
            logger.LogDebug("Adzuna skipped — AppId/AppKey not configured.");
            yield break;
        }

        var http = httpFactory.CreateClient(nameof(AdzunaSource));

        var countries = (_opts.Countries is { Length: > 0 }) ? _opts.Countries : new[] { "us" };
        var queries = (_opts.Queries is { Length: > 0 }) ? _opts.Queries : new[] { "software engineer" };

        foreach (var country in countries)
        foreach (var query in queries)
        {
            var url = BuildUrl(country, query);
            AdzunaResponse? data = null;
            try
            {
                using var resp = await http.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                var stream = await resp.Content.ReadAsStreamAsync(ct);
                data = await JsonSerializer.DeserializeAsync<AdzunaResponse>(stream, Json, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Adzuna fetch failed for {Url}", Redact(url));
            }
            if (data?.Results is null) continue;

            logger.LogInformation("Adzuna {Country} q='{Query}' results={Count}",
                country, query, data.Results.Count);

            foreach (var r in data.Results)
            {
                var listing = ToListing(r, country);
                if (listing is not null) yield return listing;
            }
        }
    }

    private string BuildUrl(string country, string query)
    {
        var q = WebUtility.UrlEncode(query);
        var maxDays = Math.Max(1, _opts.MaxDaysOld);
        return $"https://api.adzuna.com/v1/api/jobs/{country}/search/1" +
               $"?app_id={WebUtility.UrlEncode(_opts.AppId)}" +
               $"&app_key={WebUtility.UrlEncode(_opts.AppKey)}" +
               $"&what={q}" +
               $"&max_days_old={maxDays}" +
               $"&results_per_page=50" +
               $"&sort_by=date";
    }

    private static string Redact(string url) =>
        System.Text.RegularExpressions.Regex.Replace(url, "(app_id|app_key)=[^&]+", "$1=REDACTED");

    private static JobListing? ToListing(AdzunaJob j, string country)
    {
        if (string.IsNullOrWhiteSpace(j.Id) ||
            string.IsNullOrWhiteSpace(j.RedirectUrl) ||
            string.IsNullOrWhiteSpace(j.Title))
            return null;

        var location = j.Location?.DisplayName;
        var remote = (location?.Contains("remote", StringComparison.OrdinalIgnoreCase) ?? false)
                     || (j.Title.Contains("remote", StringComparison.OrdinalIgnoreCase));

        string? salary = null;
        if (j.SalaryMin.HasValue && j.SalaryMax.HasValue)
            salary = $"${j.SalaryMin:N0} - ${j.SalaryMax:N0}";
        else if (j.SalaryMin.HasValue)
            salary = $"${j.SalaryMin:N0}+";

        return new JobListing
        {
            Source = "adzuna",
            ExternalId = $"{country}:{j.Id}",
            Title = Clip(j.Title, 300)!,
            Company = Clip(j.Company?.DisplayName ?? "", 200)!,
            Location = Clip(location, 200),
            Remote = remote,
            Url = Clip(j.RedirectUrl, 800)!,
            PostedAt = j.Created ?? DateTime.UtcNow,
            FetchedAt = DateTime.UtcNow,
            Description = Clip(StripHtml(j.Description), 500),
            Tags = Clip(j.Category?.Label, 300),
            Salary = Clip(salary, 100),
        };
    }

    private static string? Clip(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length > max ? s[..max] : s);

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
    }

    private sealed class AdzunaResponse
    {
        [JsonPropertyName("results")] public List<AdzunaJob>? Results { get; set; }
        [JsonPropertyName("count")] public long Count { get; set; }
    }

    private sealed class AdzunaJob
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("redirect_url")] public string? RedirectUrl { get; set; }
        [JsonPropertyName("created")] public DateTime? Created { get; set; }
        [JsonPropertyName("salary_min")] public decimal? SalaryMin { get; set; }
        [JsonPropertyName("salary_max")] public decimal? SalaryMax { get; set; }
        [JsonPropertyName("location")] public AdzunaLocation? Location { get; set; }
        [JsonPropertyName("company")] public AdzunaCompany? Company { get; set; }
        [JsonPropertyName("category")] public AdzunaCategory? Category { get; set; }
    }

    private sealed class AdzunaLocation
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }

    private sealed class AdzunaCompany
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }

    private sealed class AdzunaCategory
    {
        [JsonPropertyName("label")] public string? Label { get; set; }
    }
}
