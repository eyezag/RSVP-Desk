using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Conversations;

/// <summary>
/// Processes stored inbound events one at a time, so messages from the same guest are always
/// handled in order. On start it picks up events left unprocessed by a previous run.
/// </summary>
public sealed class InboundWorker(
    InboundQueue queue,
    ProcessingNotifier notifier,
    IServiceScopeFactory scopes,
    ILogger<InboundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnprocessedAsync(stoppingToken);

        await foreach (var eventId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ConversationService>();
                await service.ProcessInboundEventAsync(eventId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // ProcessInboundEventAsync records its own errors; this is a last resort.
                logger.LogError(ex, "Unexpected failure processing inbound event {EventId}", eventId);
            }
            finally
            {
                notifier.Completed(eventId);
            }
        }
    }

    private async Task RequeueUnprocessedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RsvpDbContext>();
        var pending = await db.WebhookEvents
            .Where(e => e.ProcessedAt == null && e.Error == null)
            .OrderBy(e => e.Id)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in pending)
        {
            queue.Enqueue(id);
        }

        if (pending.Count > 0)
        {
            logger.LogInformation("Re-queued {Count} unprocessed inbound events", pending.Count);
        }
    }
}
