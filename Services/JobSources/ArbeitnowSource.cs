using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using jobtracker.Data;

namespace jobtracker.Services.JobSources;

public sealed class ArbeitnowSource(
    IHttpClientFactory httpFactory,
    ILogger<ArbeitnowSource> logger) : IJobSource
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private const int MaxPages = 3;

    public string Name => "arbeitnow";

    public async IAsyncEnumerable<JobListing> FetchAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var http = httpFactory.CreateClient(nameof(ArbeitnowSource));

        for (var page = 1; page <= MaxPages; page++)
        {
            var url = $"https://www.arbeitnow.com/api/job-board-api?page={page}";
            ArbeitnowResponse? data = null;
            try
            {
                using var resp = await http.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                var stream = await resp.Content.ReadAsStreamAsync(ct);
                data = await JsonSerializer.DeserializeAsync<ArbeitnowResponse>(stream, Json, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Arbeitnow fetch failed for {Url}", url);
            }

            if (data?.Data is null || data.Data.Count == 0) yield break;

            foreach (var j in data.Data)
            {
                var listing = ToListing(j);
                if (listing is not null) yield return listing;
            }
        }
    }

    private static JobListing? ToListing(ArbeitnowJob j)
    {
        if (string.IsNullOrWhiteSpace(j.Slug) || string.IsNullOrWhiteSpace(j.Url) ||
            string.IsNullOrWhiteSpace(j.Title))
            return null;

        DateTime posted = DateTime.UtcNow;
        if (j.CreatedAt.HasValue)
            posted = DateTimeOffset.FromUnixTimeSeconds(j.CreatedAt.Value).UtcDateTime;

        return new JobListing
        {
            Source = "arbeitnow",
            ExternalId = j.Slug,
            Title = Clip(j.Title, 300)!,
            Company = Clip(j.CompanyName ?? "", 200)!,
            Location = Clip(j.Location, 200),
            Remote = j.Remote ?? false,
            Url = Clip(j.Url, 800)!,
            PostedAt = posted,
            FetchedAt = DateTime.UtcNow,
            Description = Clip(StripHtml(j.Description), 500),
            Tags = j.Tags is { Count: > 0 } ? Clip(string.Join(',', j.Tags), 300) : null,
        };
    }

    private static string? Clip(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length > max ? s[..max] : s);

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
    }

    private sealed class ArbeitnowResponse
    {
        [JsonPropertyName("data")] public List<ArbeitnowJob>? Data { get; set; }
    }

    private sealed class ArbeitnowJob
    {
        [JsonPropertyName("slug")] public string? Slug { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("company_name")] public string? CompanyName { get; set; }
        [JsonPropertyName("location")] public string? Location { get; set; }
        [JsonPropertyName("remote")] public bool? Remote { get; set; }
        [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("created_at")] public long? CreatedAt { get; set; }
    }
}
