// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.Options;
using NexoPBX.Abstractions.Events;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Time;
using NexoPBX.Store.Repositories;

namespace NexoPBX.App;

/// <summary>Applies migrations before the server starts, and announces start and stop on the event bus.</summary>
internal sealed partial class HostLifecycleService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IEventBus _bus;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly DatabaseOptions _database;
    private readonly ILogger<HostLifecycleService> _logger;

    public HostLifecycleService(
        IServiceProvider services,
        IHostApplicationLifetime lifetime,
        IEventBus bus,
        IClock clock,
        IIdGenerator ids,
        IOptions<DatabaseOptions> database,
        ILogger<HostLifecycleService> logger)
    {
        _services = services;
        _lifetime = lifetime;
        _bus = bus;
        _clock = clock;
        _ids = ids;
        _database = database.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_database.AutoMigrate)
        {
            await using var scope = _services.CreateAsyncScope();
            var migrator = scope.ServiceProvider.GetRequiredService<IStoreMigrator>();
            var pending = await migrator.GetPendingMigrationsAsync(cancellationToken);
            if (pending.Count > 0)
            {
                LogMigrating(migrator.Provider, pending.Count);
                await migrator.MigrateAsync(cancellationToken);
            }
        }

        _lifetime.ApplicationStarted.Register(() => Publish(EventTypes.SystemStarted));
        _lifetime.ApplicationStopping.Register(() => Publish(EventTypes.SystemStopping));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Publish(string type) =>
        _bus.Publish(new EventEnvelope(_ids.NewId(), type, TenantIds.Default, _clock.UtcNow, null, EventEnvelope.NoData));

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Applying {Count} pending {Provider} migration(s)")]
    private partial void LogMigrating(string provider, int count);
}
