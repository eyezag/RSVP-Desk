using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using RsvpDesk.Engine;
using RsvpDesk.Web;
using RsvpDesk.Web.Api;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Channels;
using RsvpDesk.Web.Conversations;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;

// The runtime container image has no curl, so the Docker HEALTHCHECK calls the
// app itself in a lightweight probe mode.
if (args.Contains("--healthcheck-probe"))
{
    return await HealthProbe.RunAsync(Environment.GetEnvironmentVariable("HEALTHCHECK_URL") ?? "http://localhost:8080/health");
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(o =>
{
    // Everything needs a signed-in staff member unless it opts out (fallback policy below).
    o.Conventions.AllowAnonymousToPage("/Index");
    o.Conventions.AllowAnonymousToPage("/Error");
    o.Conventions.AllowAnonymousToPage("/Simulator");
    o.Conventions.AllowAnonymousToFolder("/Account");
    o.Conventions.AuthorizeFolder("/Events", Policies.Staff);
    o.Conventions.AuthorizeFolder("/Guests", Policies.Staff);
});

// In a container, TLS normally terminates at a reverse proxy. Only when the
// deployment says so do we trust its X-Forwarded-* headers (see decisions D-007).
var trustForwardedHeaders = builder.Configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders");
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddRsvpDatabase(builder.Configuration);

// ---- Staff authentication ----
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "rsvpdesk.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.ExpireTimeSpan = TimeSpan.FromHours(authOptions.SessionHours);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => ApiAwareRedirect(ctx, StatusCodes.Status401Unauthorized);
        o.Events.OnRedirectToAccessDenied = ctx => ApiAwareRedirect(ctx, StatusCodes.Status403Forbidden);
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Staff, p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Organiser), nameof(UserRole.Viewer)))
    .AddPolicy(Policies.Organiser, p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Organiser)))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(Policies.LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authOptions.LoginRateLimitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddScoped<AuditLog>();
builder.Services.AddScoped<UserService>();

// ---- Channels ----
builder.Services.Configure<ChannelOptions>(builder.Configuration.GetSection(ChannelOptions.SectionName));
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
var channelMode = builder.Configuration.GetSection(ChannelOptions.SectionName).Get<ChannelOptions>()?.Mode ?? ChannelMode.Simulator;
if (channelMode != ChannelMode.Simulator)
{
    throw new NotSupportedException($"Channel:Mode={channelMode} is not implemented yet (build step 7). Use Simulator.");
}

// ---- Conversation pipeline: pure engine + persistence + channel + background worker ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(MessageCatalog.English);
builder.Services.AddSingleton<ConversationEngine>();
builder.Services.AddSingleton<MessageRenderer>();
builder.Services.AddSingleton<IChannel, SimulatorChannel>();
builder.Services.AddSingleton<ChannelRegistry>();
builder.Services.AddSingleton<InboundQueue>();
builder.Services.AddSingleton<ProcessingNotifier>();
builder.Services.AddScoped<ConversationService>();
builder.Services.AddHostedService<InboundWorker>();

// ---- Dashboard ----
builder.Services.AddScoped<GuestQueries>();
builder.Services.AddScoped<GuestCommands>();
builder.Services.AddScoped<EventCommands>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<RsvpDbContext>("database");

var app = builder.Build();

if (trustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponse.WriteAsync }).AllowAnonymous();

app.MapGuestApi();
if (channelMode == ChannelMode.Simulator)
{
    app.MapSimulatorApi();
}

await app.MigrateRsvpDatabaseAsync();
await app.SeedDemoDataAsync();
await app.SeedInitialOrganiserAsync();

// `dotnet RsvpDesk.Web.dll --create-user <email> <Organiser|Viewer>` reads the password from
// the RSVP_NEW_USER_PASSWORD environment variable or standard input, then exits.
var createUser = Array.IndexOf(args, "--create-user");
if (createUser >= 0)
{
    return await CreateUserCommand.RunAsync(app, args.Skip(createUser + 1).ToArray());
}

await app.RunAsync();
return 0;

static Task ApiAwareRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx, int apiStatus)
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.StatusCode = apiStatus;
    }
    else
    {
        ctx.Response.Redirect(ctx.RedirectUri);
    }

    return Task.CompletedTask;
}

/// <summary>Exposed so integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
