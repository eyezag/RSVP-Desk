using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RsvpDesk.Web;

/// <summary>Small JSON body for /health. Never includes exception details.</summary>
internal static class HealthResponse
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
