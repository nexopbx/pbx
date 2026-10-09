// SPDX-License-Identifier: AGPL-3.0-only
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using NexoPBX.Abstractions.Events;
using NexoPBX.Abstractions.Security;
using NexoPBX.Api.Infrastructure;
using NexoPBX.Contracts.V1;

namespace NexoPBX.Api.Services;

/// <summary>Implements <c>nexopbx.v1.EventService</c> on top of the internal event bus.</summary>
[Authorize(Policy = Scopes.EventsSubscribe)]
public sealed class EventGrpcService : EventService.EventServiceBase
{
    private readonly IEventBus _bus;
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Creates the service.</summary>
    public EventGrpcService(IEventBus bus, IHostApplicationLifetime lifetime)
    {
        _bus = bus;
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<Event> responseStream, ServerCallContext context)
    {
        var caller = Caller.From(context);
        using var subscription = _bus.Subscribe(new SubscriptionOptions
        {
            TenantId = caller.TenantId,
            TypePrefixes = request.TypePrefixes.ToArray(),
        });

        // Streams end when the client leaves OR the host starts shutting down; otherwise they would hold up the drain.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _lifetime.ApplicationStopping);

        // Send headers now so clients know the subscription is live before the first event.
        await context.WriteResponseHeadersAsync([]);
        try
        {
            await foreach (var envelope in subscription.ReadAllAsync(linked.Token))
            {
                await responseStream.WriteAsync(Mapping.ToProto(envelope), linked.Token);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // Client went away or the host is stopping: normal end of a subscription.
        }
    }
}
