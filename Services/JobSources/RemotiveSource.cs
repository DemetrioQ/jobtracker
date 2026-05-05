using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using jobtracker.Data;
using Microsoft.Extensions.Options;

namespace jobtracker.Services.JobSources;

public sealed class RemotiveSource(
    IHttpClientFactory httpFactory,
    IOptions<JobIngestionOptions> options,
    ILogger<RemotiveSource> logger) : IJobSource
{
    private readonly JobIngestionOptions.RemotiveOptions _opts = options.Value.Remotive;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string Name => "remotive";

    public async IAsyncEnumerable<JobListing> FetchAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var http = httpFactory.CreateClient(nameof(RemotiveSource));

        var searches = (_opts.SearchTerms is { Length: > 0 })
            ? _opts.SearchTerms
            : new[] { "" };

        foreach (var search in searches)
        {
            var url = BuildUrl(search);
            RemotiveResponse? data = null;
            try
            {
                using var resp = await http.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                var stream = await resp.Content.ReadAsStreamAsync(ct);
                data = await JsonSerializer.DeserializeAsync<RemotiveResponse>(stream, Json, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Remotive fetch failed for {Url}", url);
            }
            if (data?.Jobs is null) continue;

            foreach (var j in data.Jobs)
            {
                var listing = ToListing(j);
                if (listing is not null) yield return listing;
            }
        }
    }

    private string BuildUrl(string search)
    {
        var category = string.IsNullOrWhiteSpace(_opts.Category) ? "software-dev" : _opts.Category;
        var c = WebUtility.UrlEncode(category);
        if (string.IsNullOrWhiteSpace(search))
            return $"https://remotive.com/api/remote-jobs?category={c}";
        return $"https://remotive.com/api/remote-jobs?category={c}&search={WebUtility.UrlEncode(search)}";
    }

    private JobListing? ToListing(RemotiveJob j)
    {
        if (string.IsNullOrWhiteSpace(j.Url) || string.IsNullOrWhiteSpace(j.Title) || j.Id == 0)
            return null;

        return new JobListing
        {
            Source = Name,
            ExternalId = j.Id.ToString(),
            Title = Clip(j.Title, 300)!,
            Company = Clip(j.CompanyName ?? "", 200)!,
            Location = Clip(j.CandidateRequiredLocation, 200),
            Remote = true,
            Url = Clip(j.Url, 800)!,
            PostedAt = j.PublicationDate ?? DateTime.UtcNow,
            FetchedAt = DateTime.UtcNow,
            Description = Clip(StripHtml(j.Description), 500),
            Tags = j.Tags is { Count: > 0 } ? Clip(string.Join(',', j.Tags), 300) : null,
            Salary = Clip(j.Salary, 100),
        };
    }

    private static string? Clip(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length > max ? s[..max] : s);

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
    }

    private sealed class RemotiveResponse
    {
        [JsonPropertyName("jobs")]
        public List<RemotiveJob>? Jobs { get; set; }
    }

    private sealed class RemotiveJob
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("company_name")] public string? CompanyName { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
        [JsonPropertyName("publication_date")] public DateTime? PublicationDate { get; set; }
        [JsonPropertyName("candidate_required_location")] public string? CandidateRequiredLocation { get; set; }
        [JsonPropertyName("salary")] public string? Salary { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }
}
