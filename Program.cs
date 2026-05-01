using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
if (!string.IsNullOrWhiteSpace(githubClientId) && !string.IsNullOrWhiteSpace(githubClientSecret))
{
    builder.Services.AddSingleton<OAuthBackchannelLogger>();
    builder.Services.AddAuthentication().AddGitHub(options =>
    {
        options.ClientId = githubClientId;
        options.ClientSecret = githubClientSecret;
        options.Scope.Add("user:email");
        options.SaveTokens = true;
        options.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OAuthDiag");
            logger.LogWarning("CHALLENGE — Scheme={Scheme} Host={Host} RedirectUri={Uri}",
                context.Request.Scheme, context.Request.Host.Value, context.RedirectUri);
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OAuthDiag");
            logger.LogError(context.Failure, "REMOTE FAILURE on callback — Scheme={Scheme} Host={Host} PathBase={PathBase} Path={Path} Query={Query}",
                context.Request.Scheme, context.Request.Host.Value, context.Request.PathBase, context.Request.Path, context.Request.QueryString);
            return Task.CompletedTask;
        };
    });

    builder.Services.AddSingleton<IConfigureOptions<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions>, ConfigureGitHubBackchannel>();
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

// In production the container is only ever reachable via Caddy over HTTPS,
// so force scheme=https unconditionally. This guarantees OAuth redirect_uri,
// cookie Secure flag, and BuildRedirectUri() all match the public origin
// without depending on Caddy actually sending X-Forwarded-Proto.
if (!app.Environment.IsDevelopment())
{
    app.Use((context, next) =>
    {
        context.Request.Scheme = "https";
        if (context.Request.Headers.TryGetValue("X-Forwarded-Host", out var host) && !string.IsNullOrEmpty(host))
        {
            context.Request.Host = new HostString(host.ToString().Split(',')[0].Trim());
        }

        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/signin-github") || path.StartsWith("/Account"))
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ReqDiag");
            logger.LogWarning("REQ {Method} Scheme={Scheme} Host={Host} PathBase={PathBase} Path={Path} Query={Query}",
                context.Request.Method, context.Request.Scheme, context.Request.Host.Value,
                context.Request.PathBase, context.Request.Path, context.Request.QueryString);
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

public class OAuthBackchannelLogger : DelegatingHandler
{
    private readonly ILogger<OAuthBackchannelLogger> _logger;
    public OAuthBackchannelLogger(ILogger<OAuthBackchannelLogger> logger)
    {
        _logger = logger;
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = "";
        if (request.Content is not null)
        {
            try
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
                body = Regex.Replace(body, @"client_secret=[^&]+", "client_secret=REDACTED");
            }
            catch { }
        }
        _logger.LogWarning("OUTBOUND {Method} {Url} BODY={Body}", request.Method, request.RequestUri, body);
        var response = await base.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode || (response.Content?.Headers?.ContentType?.MediaType?.Contains("json") ?? false))
        {
            try
            {
                var respBody = await response.Content!.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("RESPONSE {Status} BODY={Body}", (int)response.StatusCode, respBody);
                response.Content = new StringContent(respBody, System.Text.Encoding.UTF8, response.Content.Headers.ContentType?.MediaType ?? "application/json");
            }
            catch { }
        }
        return response;
    }
}

public class ConfigureGitHubBackchannel : IConfigureOptions<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions>
{
    private readonly OAuthBackchannelLogger _handler;
    public ConfigureGitHubBackchannel(OAuthBackchannelLogger handler) => _handler = handler;
    public void Configure(AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions options)
    {
        options.Backchannel = new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
}
