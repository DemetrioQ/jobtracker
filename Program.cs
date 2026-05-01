using System.Text;
using System.Text.RegularExpressions;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using jobtracker.Components;
using jobtracker.Components.Account;
using jobtracker.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var githubClientId = builder.Configuration["Authentication:GitHub:ClientId"];
var githubClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"];
var publicBaseUrl = builder.Configuration["App:PublicUrl"]?.TrimEnd('/');
var fixedRedirectUri = !string.IsNullOrWhiteSpace(publicBaseUrl) ? $"{publicBaseUrl}/signin-github" : null;

if (!string.IsNullOrWhiteSpace(githubClientId) && !string.IsNullOrWhiteSpace(githubClientSecret))
{
    builder.Services.AddSingleton<OAuthBackchannelHandler>();

    builder.Services.AddAuthentication().AddGitHub(options =>
    {
        options.ClientId = githubClientId;
        options.ClientSecret = githubClientSecret;
        options.Scope.Add("user:email");
        options.SaveTokens = true;
        options.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OAuthDiag");
            // Force redirect_uri in the challenge URL to the canonical public URL.
            // Belt-and-suspenders: even if Request.Scheme/Host happens to be wrong
            // for the POST that initiated the challenge, the URL we send GitHub
            // to redirect back to is always https://jobs.demetrioq.com/signin-github.
            var redirectUri = context.RedirectUri;
            if (fixedRedirectUri != null)
            {
                redirectUri = Regex.Replace(redirectUri, @"redirect_uri=[^&]*",
                    $"redirect_uri={Uri.EscapeDataString(fixedRedirectUri)}");
            }
            logger.LogWarning("CHALLENGE (final URL) — {Uri}", redirectUri);
            context.Response.Redirect(redirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OAuthDiag");
            logger.LogError(context.Failure, "REMOTE FAILURE — Scheme={Scheme} Host={Host} PathBase={PathBase} Path={Path} Query={Query}",
                context.Request.Scheme, context.Request.Host.Value, context.Request.PathBase, context.Request.Path, context.Request.QueryString);
            return Task.CompletedTask;
        };
    });

    // Use AddOptions<>(scheme).Configure<dep>() so the post-configure runs for the
    // *named* "GitHub" options instance, not the unnamed default. IConfigureOptions<>
    // alone silently no-ops for named-options scenarios like authentication schemes.
    builder.Services.AddOptions<GitHubAuthenticationOptions>(GitHubAuthenticationDefaults.AuthenticationScheme)
        .Configure<OAuthBackchannelHandler>((options, handler) =>
        {
            options.Backchannel = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        });
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

if (!builder.Environment.IsDevelopment())
{
    var keysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/app/Data/keys";
    Directory.CreateDirectory(keysPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
        .SetApplicationName("jobtracker");
}

var app = builder.Build();

Console.WriteLine($"=== Environment: {app.Environment.EnvironmentName} ===");
Console.WriteLine($"=== App:PublicUrl: {publicBaseUrl ?? "(unset)"} ===");

if (!app.Environment.IsDevelopment())
{
    app.Use((context, next) =>
    {
        context.Request.Scheme = "https";
        if (context.Request.Headers.TryGetValue("X-Forwarded-Host", out var host) && !string.IsNullOrEmpty(host))
        {
            context.Request.Host = new HostString(host.ToString().Split(',')[0].Trim());
        }
        return next();
    });
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
    app.UseHttpsRedirection();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();

public class OAuthBackchannelHandler : DelegatingHandler
{
    private readonly ILogger<OAuthBackchannelHandler> _logger;
    private readonly string? _fixedRedirectUri;

    public OAuthBackchannelHandler(ILogger<OAuthBackchannelHandler> logger, IConfiguration config)
    {
        _logger = logger;
        var baseUrl = config["App:PublicUrl"]?.TrimEnd('/');
        _fixedRedirectUri = !string.IsNullOrWhiteSpace(baseUrl) ? $"{baseUrl}/signin-github" : null;
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isTokenEndpoint = request.RequestUri?.ToString().Contains("/login/oauth/access_token", StringComparison.OrdinalIgnoreCase) == true;

        if (isTokenEndpoint && request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            var redactedBefore = Redact(body);
            string finalBody = body;

            if (_fixedRedirectUri != null)
            {
                finalBody = Regex.Replace(body, @"redirect_uri=[^&]*",
                    $"redirect_uri={Uri.EscapeDataString(_fixedRedirectUri)}");
            }

            _logger.LogWarning("OAUTH OUTBOUND {Url}", request.RequestUri);
            _logger.LogWarning("OAUTH BODY before: {Body}", redactedBefore);
            _logger.LogWarning("OAUTH BODY after:  {Body}", Redact(finalBody));

            var contentType = request.Content.Headers.ContentType?.MediaType ?? "application/x-www-form-urlencoded";
            request.Content = new StringContent(finalBody, Encoding.UTF8, contentType);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (isTokenEndpoint && response.Content != null)
        {
            try
            {
                var respBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("OAUTH RESPONSE {Status}: {Body}", (int)response.StatusCode, respBody);
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
                response.Content = new StringContent(respBody, Encoding.UTF8, mediaType);
            }
            catch { }
        }

        return response;
    }

    private static string Redact(string body) =>
        Regex.Replace(body, @"client_secret=[^&]+", "client_secret=REDACTED");
}
