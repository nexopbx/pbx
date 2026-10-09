// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexoPBX.Abstractions.Events;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Security;
using NexoPBX.Abstractions.Time;
using NexoPBX.Auth.Http;
using NexoPBX.Auth.Passwords;
using NexoPBX.Auth.Tokens;
using NexoPBX.Auth.Totp;

namespace NexoPBX.Auth;

/// <summary>Registers authentication, authorization policies and the services behind them.</summary>
public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Adds the NexoPBX token authentication scheme, one authorization policy per scope (policy name = scope
    /// name, see <see cref="Scopes"/>), and the auth services. Data protection must be configured by the host.
    /// </summary>
    public static IServiceCollection AddNexoAuth(this IServiceCollection services, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<AuthOptions>();
        if (configuration is not null)
        {
            options.Bind(configuration.GetSection(AuthOptions.SectionName));
        }

        services.TryAddSingleton<IClock>(NexoPBX.Abstractions.Time.SystemClock.Instance);
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddSingleton<IEventBus>(sp => new InMemoryEventBus(sp.GetRequiredService<IClock>(), sp.GetRequiredService<IIdGenerator>()));
        services.AddDataProtection();

        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<PasswordPolicy>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<TokenService>();
        services.AddScoped<TokenAuthenticator>();
        services.AddScoped<AuthService>();

        services.AddAuthentication(NexoAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, NexoTokenAuthenticationHandler>(NexoAuthenticationDefaults.Scheme, _ => { });
        services.AddAuthorization(o =>
        {
            foreach (var scope in Scopes.All)
            {
                o.AddPolicy(scope, p => p
                    .AddAuthenticationSchemes(NexoAuthenticationDefaults.Scheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim(NexoClaims.Scope, scope));
            }
        });
        return services;
    }
}
