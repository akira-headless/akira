using System.Text.Json;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Akira.Server.Events;

public sealed class EventPublisher : IEventPublisher
{
    private const int BatchSize = 500;

    // One writer at a time, from insert through broadcast. On MariaDB two transactions can commit in
    // the other order than they got their auto increment value, and a client resuming after the higher
    // number would never see the lower one. Serialized, sequence order is commit order.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EventHub _hub;
    private readonly EventSignal _signal;
    private readonly IOptions<JsonOptions> _jsonOptions;
    private readonly TimeProvider _time;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<EventPublisher> _logger;

    public EventPublisher(
        IServiceScopeFactory scopeFactory,
        EventHub hub,
        EventSignal signal,
        IOptions<JsonOptions> jsonOptions,
        TimeProvider time,
        IHostApplicationLifetime lifetime,
        ILogger<EventPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _signal = signal;
        _jsonOptions = jsonOptions;
        _time = time;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync(IReadOnlyList<EventDraft> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        var stopping = _lifetime.ApplicationStopping;
        await _gate.WaitAsync(stopping);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();
            var options = _jsonOptions.Value.SerializerOptions;
            var now = _time.GetUtcNow();

            foreach (var batch in events.Chunk(BatchSize))
            {
                var rows = batch.Select(draft => new OutboxEvent
                {
                    Type = draft.Type,
                    Version = draft.Version,
                    Payload = JsonSerializer.Serialize(draft.Payload, draft.Payload.GetType(), options),
                    UserId = draft.UserId,
                    CreatedAt = now
                }).ToList();

                // Inserted in the order they were added, so the sequences follow the order they were drafted in
                dbContext.Events.AddRange(rows);
                await dbContext.SaveChangesAsync(stopping);
                dbContext.ChangeTracker.Clear();

                var hubEvents = rows.Select(row => new HubEvent(row.Sequence, row.UserId, Serialize(row, options))).ToList();
                _hub.Broadcast(hubEvents);
            }

            _signal.Pulse();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The change itself is saved. Failing the request for it would make the client retry something that worked.
            _logger.LogError(exception, "Could not store {Count} events, clients that reconnect will not see them", events.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static EventMessage ToMessage(OutboxEvent row)
    {
        using var payload = JsonDocument.Parse(row.Payload);
        return new EventMessage(row.Sequence, row.Type, row.Version, payload.RootElement.Clone(), row.CreatedAt);
    }

    private static byte[] Serialize(OutboxEvent row, JsonSerializerOptions options) =>
        JsonSerializer.SerializeToUtf8Bytes(ToMessage(row), options);
}
