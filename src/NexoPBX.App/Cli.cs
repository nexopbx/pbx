// SPDX-License-Identifier: AGPL-3.0-only
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexoPBX.Abstractions.Security;
using NexoPBX.Auth;
using NexoPBX.Auth.Tokens;
using NexoPBX.Store;
using NexoPBX.Store.Entities;
using NexoPBX.Store.Repositories;

namespace NexoPBX.App;

/// <summary>Implementations of the command-line verbs. Exit codes: 0 ok, 1 usage/config error, 2 failure, 3 already running.</summary>
internal static class Cli
{
    public const int ExitOk = 0;
    public const int ExitConfig = 1;
    public const int ExitFailure = 2;
    public const int ExitAlreadyRunning = 3;

    public static async Task<int> RunServerAsync(NexoPaths paths, bool serviceMode, CancellationToken cancellationToken)
    {
        using var instanceLock = InstanceLock.TryAcquire(paths.DataDirectory);
        if (instanceLock is null)
        {
            await Console.Error.WriteLineAsync($"Another NexoPBX instance is already using '{paths.DataDirectory}'.");
            return ExitAlreadyRunning;
        }

        WebApplication app;
        try
        {
            app = NexoHost.Build(paths, "headless", serviceMode);
        }
        catch (InvalidOperationException ex)
        {
            await Console.Error.WriteLineAsync($"Configuration error: {ex.Message}");
            return ExitConfig;
        }

        await app.RunAsync(cancellationToken);
        return ExitOk;
    }

    public static Task<int> DbMigrateAsync(NexoPaths paths, CancellationToken cancellationToken) =>
        WithServicesAsync(paths, needsSchema: false, action: async sp =>
        {
            var migrator = sp.GetRequiredService<IStoreMigrator>();
            var pending = await migrator.GetPendingMigrationsAsync(cancellationToken);
            if (pending.Count == 0)
            {
                Console.WriteLine($"{migrator.Provider}: already up to date.");
                return ExitOk;
            }

            await migrator.MigrateAsync(cancellationToken);
            Console.WriteLine($"{migrator.Provider}: applied {pending.Count} migration(s): {string.Join(", ", pending)}");
            return ExitOk;
        });

    public static Task<int> DbStatusAsync(NexoPaths paths, CancellationToken cancellationToken) =>
        WithServicesAsync(paths, needsSchema: false, action: async sp =>
        {
            var migrator = sp.GetRequiredService<IStoreMigrator>();
            if (!await migrator.CanConnectAsync(cancellationToken))
            {
                await Console.Error.WriteLineAsync($"{migrator.Provider}: cannot connect to the database.");
                return ExitFailure;
            }

            var pending = await migrator.GetPendingMigrationsAsync(cancellationToken);
            Console.WriteLine(pending.Count == 0
                ? $"{migrator.Provider}: up to date."
                : $"{migrator.Provider}: {pending.Count} pending: {string.Join(", ", pending)}");
            return pending.Count == 0 ? ExitOk : ExitFailure;
        });

    public static Task<int> UserBootstrapAsync(NexoPaths paths, string username, string? displayName, bool passwordFromStdin, CancellationToken cancellationToken) =>
        WithServicesAsync(paths, needsSchema: true, action: async sp =>
        {
            var password = passwordFromStdin
                ? await Console.In.ReadLineAsync(cancellationToken)
                : Environment.GetEnvironmentVariable("NEXOPBX_BOOTSTRAP_PASSWORD");
            if (string.IsNullOrEmpty(password))
            {
                await Console.Error.WriteLineAsync("Provide the password with --password-stdin or the NEXOPBX_BOOTSTRAP_PASSWORD environment variable (never on the command line: it would end up in shell history).");
                return ExitConfig;
            }

            try
            {
                var user = await sp.GetRequiredService<AuthService>().BootstrapOwnerAsync(username, displayName ?? username, password, cancellationToken);
                Console.WriteLine($"Created Owner '{user.Username}' ({user.Id}).");
                return ExitOk;
            }
            catch (Exception ex) when (ex is NexoValidationException or InvalidOperationException or DuplicateException)
            {
                await Console.Error.WriteLineAsync(ex.Message);
                return ExitFailure;
            }
        });

    public static Task<int> TokenCreateAsync(NexoPaths paths, string username, string name, string[] scopes, int days, CancellationToken cancellationToken) =>
        WithServicesAsync(paths, needsSchema: true, action: async sp =>
        {
            var user = await sp.GetRequiredService<IUserRepository>().FindByUsernameAsync(username, cancellationToken);
            if (user is null)
            {
                await Console.Error.WriteLineAsync($"No user named '{username}'.");
                return ExitFailure;
            }

            // Same rule as the API: a token is never stronger than the user it acts as.
            var allowed = user.Role == Role.ApiClient ? Scopes.All : Scopes.ForRole(user.Role);
            var tooMuch = scopes.Where(s => !allowed.Contains(s)).ToList();
            if (tooMuch.Count > 0)
            {
                await Console.Error.WriteLineAsync($"User '{username}' ({user.Role}) cannot hold: {string.Join(", ", tooMuch)}. Known scopes: {string.Join(", ", Scopes.All.Order())}");
                return ExitFailure;
            }

            try
            {
                var issued = await sp.GetRequiredService<TokenService>().IssueAsync(
                    user.Id, name, scopes, TokenKind.Api, days > 0 ? TimeSpan.FromDays(days) : null, cancellationToken);
                await sp.GetRequiredService<IAuditWriter>().WriteAsync(
                    new AuditEntry("token.create", AuditOutcome.Success, user.Id, TargetType: "token", TargetId: issued.Entity.Id.ToString(), Details: new Dictionary<string, string> { ["via"] = "cli" }),
                    cancellationToken);
                Console.WriteLine(issued.Plaintext);
                await Console.Error.WriteLineAsync("Token created. This is the only time it is shown; store it safely.");
                return ExitOk;
            }
            catch (NexoValidationException ex)
            {
                await Console.Error.WriteLineAsync(ex.Message);
                return ExitFailure;
            }
        });

    public static int PrintVersion()
    {
        var version = typeof(Cli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        Console.WriteLine($"nexopbxd {version.Split('+')[0]}");
        return ExitOk;
    }

    /// <summary>
    /// Builds the services (without starting any server) and runs an action in a scope. Logs stay quiet.
    /// Commands that need the schema behave like the server: they apply pending migrations when
    /// <c>database.auto_migrate</c> is on, and otherwise say what to run instead of failing with a stack trace.
    /// </summary>
    private static async Task<int> WithServicesAsync(NexoPaths paths, bool needsSchema, Func<IServiceProvider, Task<int>> action)
    {
        WebApplication app;
        try
        {
            app = NexoHost.Build(paths, configure: c => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["logging:level"] = "Warning",
                ["logging:format"] = "text",
            }));
        }
        catch (InvalidOperationException ex)
        {
            await Console.Error.WriteLineAsync($"Configuration error: {ex.Message}");
            return ExitConfig;
        }

        await using (app)
        {
            await using var scope = app.Services.CreateAsyncScope();
            try
            {
                if (needsSchema)
                {
                    var migrator = scope.ServiceProvider.GetRequiredService<IStoreMigrator>();
                    var pending = await migrator.GetPendingMigrationsAsync(CancellationToken.None);
                    if (pending.Count > 0)
                    {
                        if (!scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.AutoMigrate)
                        {
                            await Console.Error.WriteLineAsync($"The database has {pending.Count} pending migration(s). Run 'nexopbxd db migrate' first.");
                            return ExitFailure;
                        }

                        await migrator.MigrateAsync(CancellationToken.None);
                        await Console.Error.WriteLineAsync($"{migrator.Provider}: applied {pending.Count} migration(s).");
                    }
                }

                return await action(scope.ServiceProvider);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await Console.Error.WriteLineAsync($"Failed: {ex.Message}");
                return ExitFailure;
            }
        }
    }
}
