using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using RsvpDesk.Web;
using RsvpDesk.Web.Data;

// The runtime container image has no curl, so the Docker HEALTHCHECK calls the
// app itself in a lightweight probe mode.
if (args.Contains("--healthcheck-probe"))
{
    return await HealthProbe.RunAsync(Environment.GetEnvironmentVariable("HEALTHCHECK_URL") ?? "http://localhost:8080/health");
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

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
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponse.WriteAsync });

await app.MigrateRsvpDatabaseAsync();

await app.RunAsync();
return 0;

/// <summary>Exposed so integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
