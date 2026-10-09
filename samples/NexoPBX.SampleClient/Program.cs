// SPDX-License-Identifier: AGPL-3.0-only
//
// NexoPBX.SampleClient: proves that everything the product does is available through the public nexopbx.v1 contract.
// It references NexoPBX.Contracts and nothing else from the server (an architecture test enforces that).
//
//   dotnet run -- --url http://127.0.0.1:8081 --token nxp_...            show system info and users
//   dotnet run -- --token nxp_... --watch 30                              also stream live events for 30 seconds
//   dotnet run -- --token nxp_... --selftest                              self-checking run used by CI
//
// --url defaults to $NEXOPBX_URL or the native-gRPC listener http://127.0.0.1:8081; --token defaults to $NEXOPBX_TOKEN.
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Net.Client;
using NexoPBX.Contracts.V1;

const int ExitOk = 0;
const int ExitUsage = 1;
const int ExitFailure = 2;

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var url = Option("--url") ?? Environment.GetEnvironmentVariable("NEXOPBX_URL") ?? "http://127.0.0.1:8081";
var token = Option("--token") ?? Environment.GetEnvironmentVariable("NEXOPBX_TOKEN");
var selfTest = args.Contains("--selftest");
var watchSeconds = int.TryParse(Option("--watch"), out var w) ? w : 0;

if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Missing token. Pass --token nxp_... or set NEXOPBX_TOKEN. Create one with: nexopbxd token create --user <name> --name sample --scope system.read --scope users.read --scope events.subscribe");
    return ExitUsage;
}

var headers = new Metadata { { "authorization", $"Bearer {token}" } };
using var channel = GrpcChannel.ForAddress(url);

try
{
    var info = await new SystemService.SystemServiceClient(channel).GetInfoAsync(new GetInfoRequest(), headers);
    Console.WriteLine($"Connected to {info.Product} {info.Version} ({info.RunMode}, {info.DatabaseProvider}) on {info.Os}, up {info.Uptime.ToTimeSpan():c}");

    var users = new UserService.UserServiceClient(channel);
    var page = await users.ListUsersAsync(new ListUsersRequest { PageSize = 20 }, headers);
    Console.WriteLine($"{page.Users.Count} user(s):");
    foreach (var user in page.Users)
    {
        Console.WriteLine($"  {user.Username,-20} {user.Role,-16} {(user.IsActive ? "active" : "inactive")}");
    }

    if (!selfTest && watchSeconds <= 0)
    {
        return ExitOk;
    }

    // Server-streaming subscription: the same stream the web UI and desktop app use.
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(selfTest ? 15 : watchSeconds));
    using var call = new EventService.EventServiceClient(channel).Subscribe(new SubscribeRequest(), headers, cancellationToken: cts.Token);
#pragma warning disable VSTHRD003 // the call object owns this task; there is no context to deadlock
    await call.ResponseHeadersAsync; // headers arrive once the subscription is live
#pragma warning restore VSTHRD003
    Console.WriteLine("Listening for events...");

    if (selfTest)
    {
        // Provoke one audit event (a failed login) and make sure it comes back through the stream.
        try
        {
            await users.LoginAsync(new LoginRequest { Username = "selftest-nobody", Password = "wrong" });
        }
        catch (RpcException)
        {
            // expected: the point is the audit event it produces
        }
    }

    try
    {
        while (await call.ResponseStream.MoveNext(cts.Token))
        {
            var e = call.ResponseStream.Current;
            Console.WriteLine($"  [{e.OccurredAt.ToDateTime():HH:mm:ss}] {e.Type} {string.Join(' ', e.Data.Select(kv => $"{kv.Key}={kv.Value}"))}");
            if (selfTest && e.Type == "audit.recorded")
            {
                Console.WriteLine("Self-test passed.");
                return ExitOk;
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
    {
    }

    if (selfTest)
    {
        Console.Error.WriteLine("Self-test failed: the expected audit event never arrived.");
        return ExitFailure;
    }

    return ExitOk;
}
catch (RpcException ex)
{
    var reason = ex.GetRpcStatus()?.Details.FirstOrDefault(d => d.Is(ErrorInfo.Descriptor))?.Unpack<ErrorInfo>().Reason;
    Console.Error.WriteLine($"Request failed: {ex.StatusCode}{(reason is null ? string.Empty : $" ({reason})")}: {ex.Status.Detail}");
    return ExitFailure;
}
