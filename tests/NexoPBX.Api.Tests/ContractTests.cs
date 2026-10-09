// SPDX-License-Identifier: AGPL-3.0-only
using System.Reflection;
using System.Text.Json.Nodes;
using Google.Api;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using NexoPBX.Abstractions.Security;
using NexoPBX.Api.Services;
using NexoPBX.Contracts.V1;

namespace NexoPBX.Api.Tests;

/// <summary>The contract's promises that no behavioural test would notice breaking.</summary>
public sealed class ContractTests
{
    private static readonly (ServiceDescriptor Descriptor, Type Implementation)[] Services =
    [
        (SystemService.Descriptor, typeof(SystemGrpcService)),
        (UserService.Descriptor, typeof(UserGrpcService)),
        (EventService.Descriptor, typeof(EventGrpcService)),
    ];

    private static IEnumerable<MethodInfo> RpcMethods(Type implementation) =>
        implementation.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetBaseDefinition().DeclaringType != m.DeclaringType && m.GetBaseDefinition().DeclaringType!.Name.EndsWith("Base", StringComparison.Ordinal));

    [Fact]
    public void Every_rpc_is_either_authorised_by_a_known_scope_or_explicitly_anonymous()
    {
        var anonymous = new List<string>();
        var problems = new List<string>();

        foreach (var (descriptor, implementation) in Services)
        {
            var methods = RpcMethods(implementation).ToList();
            Assert.Equal(descriptor.Methods.Count, methods.Count); // every proto RPC is implemented, and nothing else is public

            foreach (var method in methods)
            {
                var name = $"{descriptor.Name}.{method.Name}";
                var attributes = method.GetCustomAttributes<AuthorizeAttribute>(true).Concat(implementation.GetCustomAttributes<AuthorizeAttribute>(true)).ToList();
                if (method.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                {
                    anonymous.Add(name);
                }
                else if (attributes.Count == 0)
                {
                    problems.Add($"{name} has no [Authorize] and no [AllowAnonymous]: ASP.NET would serve it to anyone");
                }
                else if (attributes.Any(a => !string.IsNullOrEmpty(a.Policy) && !Scopes.All.Contains(a.Policy)))
                {
                    problems.Add($"{name} requires an unknown policy");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Equal(["UserService.Login"], anonymous); // the only public call is the one that creates sessions
    }

    [Fact]
    public void Every_rpc_is_reachable_over_rest_and_documented_in_openapi()
    {
        var document = NexoApiExtensions.OpenApiDocument();
        var operationIds = document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject().Select(verb => verb.Value!["operationId"]!.GetValue<string>()))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (descriptor, _) in Services)
        {
            foreach (var method in descriptor.Methods)
            {
                Assert.NotNull(method.GetOptions()?.GetExtension(AnnotationsExtensions.Http));
                Assert.Contains($"{descriptor.Name}_{method.Name}", operationIds);
            }
        }
    }

    [Fact]
    public void The_openapi_document_is_31_and_all_references_resolve()
    {
        var document = NexoApiExtensions.OpenApiDocument();
        var schemas = document["components"]!["schemas"]!.AsObject();
        var unresolved = new List<string>();

        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key == "$ref" && value!.GetValue<string>() is { } reference && !schemas.ContainsKey(reference.Split('/')[^1]))
                        {
                            unresolved.Add(reference);
                        }

                        Walk(value);
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        Walk(item);
                    }

                    break;
            }
        }

        Walk(document);

        Assert.StartsWith("3.1", document["openapi"]!.GetValue<string>());
        Assert.True(unresolved.Count == 0, $"Dangling $ref: {string.Join(", ", unresolved.Distinct())}");
    }

    [Fact]
    public void Operations_declare_the_scopes_that_the_code_actually_enforces()
    {
        var document = NexoApiExtensions.OpenApiDocument();
        var users = document["paths"]!["/v1/users"]!.AsObject();

        Assert.Equal("users.read", users["get"]!["x-required-scopes"]![0]!.GetValue<string>());
        Assert.Equal("users.write", users["post"]!["x-required-scopes"]![0]!.GetValue<string>());
        Assert.Empty(document["paths"]!["/v1/auth:login"]!["post"]!["security"]!.AsArray());
    }

    [Fact]
    public void Every_error_a_client_can_see_has_a_documented_reason_code()
    {
        // The reasons promised in the proto comments must exist in the implementation, so the docs cannot lie.
        var source = string.Concat(Directory.GetFiles(FindApiSources(), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        var protoText = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "NexoPBX.Contracts", "Protos", "nexopbx", "v1", "user.proto"));
        foreach (var reason in new[] { "INVALID_CREDENTIALS", "ACCOUNT_LOCKED", "TOTP_REQUIRED", "USERNAME_TAKEN", "ETAG_MISMATCH" })
        {
            Assert.Contains(reason, protoText, StringComparison.Ordinal);
            Assert.Contains($"\"{reason}\"", source, StringComparison.Ordinal);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexoPBX.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private static string FindApiSources() => Path.Combine(FindRepoRoot(), "src", "NexoPBX.Api");
}
