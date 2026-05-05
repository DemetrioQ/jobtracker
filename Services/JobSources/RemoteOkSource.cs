using System.Runtime.CompilerServices;
using System.Text.Json;
using jobtracker.Data;

namespace jobtracker.Services.JobSources;

public sealed class RemoteOkSource(
    IHttpClientFactory httpFactory,
    ILogger<RemoteOkSource> logger) : IJobSource
{
    private const string Endpoint = "https://remoteok.com/api";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string Name => "remoteok";

    public async IAsyncEnumerable<JobListing> FetchAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var http = httpFactory.CreateClient(nameof(RemoteOkSource));

        List<JsonElement>? items = null;
        try
        {
            using var resp = await http.GetAsync(Endpoint, ct);
            resp.EnsureSuccessStatusCode();
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            items = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream, Json, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Remote OK fetch failed");
        }
        if (items is null) yield break;

        foreach (var el in items)
        {
            // First element is a "legal" disclaimer object — has no `id`/`position`.
            if (!el.TryGetProperty("id", out _) || !el.TryGetProperty("position", out _))
                continue;

            var listing = ToListing(el);
            if (listing is not null) yield return listing;
        }
    }

    private static JobListing? ToListing(JsonElement el)
    {
        var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var url = el.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
        var title = el.TryGetProperty("position", out var posEl) ? posEl.GetString() : null;
        var company = el.TryGetProperty("company", out var coEl) ? coEl.GetString() : null;
        var location = el.TryGetProperty("location", out var locEl) ? locEl.GetString() : null;
        var description = el.TryGetProperty("description", out var dEl) ? dEl.GetString() : null;
        var date = el.TryGetProperty("date", out var dtEl) ? dtEl.GetString() : null;
        var salaryMin = el.TryGetProperty("salary_min", out var smEl) && smEl.ValueKind == JsonValueKind.Number ? smEl.GetInt64() : 0;
        var salaryMax = el.TryGetProperty("salary_max", out var sxEl) && sxEl.ValueKind == JsonValueKind.Number ? sxEl.GetInt64() : 0;

        var tags = new List<string>();
        if (el.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tagsEl.EnumerateArray())
            {
                if (t.ValueKind == JsonValueKind.String) tags.Add(t.GetString()!);
            }
        }

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title))
            return null;

        var posted = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(date) && DateTimeOffset.TryParse(date, out var dto))
            posted = dto.UtcDateTime;

        string? salary = null;
        if (salaryMin > 0 && salaryMax > 0) salary = $"${salaryMin:N0} - ${salaryMax:N0}";
        else if (salaryMin > 0) salary = $"${salaryMin:N0}+";

        return new JobListing
        {
            Source = "remoteok",
            ExternalId = id,
            Title = Clip(title, 300)!,
            Company = Clip(company ?? "", 200)!,
            Location = Clip(location, 200),
            Remote = true,
            Url = Clip(url, 800)!,
            PostedAt = posted,
            FetchedAt = DateTime.UtcNow,
            Description = Clip(StripHtml(description), 500),
            Tags = tags.Count > 0 ? Clip(string.Join(',', tags), 300) : null,
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
}
