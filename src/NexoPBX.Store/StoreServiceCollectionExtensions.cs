// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.DependencyInjection;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Store.Repositories;

namespace NexoPBX.Store;

/// <summary>Registers the provider-agnostic parts of the store.</summary>
public static class StoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers repositories and the migrator. A provider package (<c>NexoPBX.Store.Sqlite</c> or
    /// <c>NexoPBX.Store.Postgres</c>) must add the <see cref="NexoDbContext"/> itself.
    /// </summary>
    public static IServiceCollection AddNexoStoreCore(this IServiceCollection services, string providerName)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped(sp => (TenantContext)sp.GetRequiredService<ITenantContext>());
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<ITokenRepository, EfTokenRepository>();
        services.AddScoped<IAuditRepository, EfAuditRepository>();
        services.AddScoped<IStoreMigrator>(sp => new EfStoreMigrator(sp.GetRequiredService<NexoDbContext>(), providerName));
        return services;
    }
}
