// SPDX-License-Identifier: AGPL-3.0-only
using System.Reflection;
using System.Text.Json.Nodes;
using Google.Api;
using Google.Protobuf.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace NexoPBX.Api.OpenApi;

/// <summary>
/// Builds the OpenAPI 3.1 document of the REST surface straight from the compiled <c>.proto</c> descriptors
/// (their <c>google.api.http</c> rules), so the document can never drift from the contract. Which scope each call
/// needs is read from the <c>[Authorize]</c> attributes of the implementing service classes.
/// </summary>
public static class ProtoOpenApiGenerator
{
    private const string BearerScheme = "bearerAuth";

    /// <summary>A proto service together with the class that implements it.</summary>
    public sealed record ServiceBinding(ServiceDescriptor Descriptor, Type Implementation);

    /// <summary>Generates the document.</summary>
    public static JsonObject Generate(IEnumerable<ServiceBinding> services, string title, string version, IEnumerable<JsonObject>? extraPaths = null)
    {
        var paths = new JsonObject();
        var schemas = new JsonObject();
        var tags = new JsonArray();

        foreach (var binding in services.OrderBy(s => s.Descriptor.FullName, StringComparer.Ordinal))
        {
            tags.Add(new JsonObject { ["name"] = binding.Descriptor.Name });
            foreach (var method in binding.Descriptor.Methods)
            {
                var rule = method.GetOptions()?.GetExtension(AnnotationsExtensions.Http);
                if (rule is null || rule.PatternCase == HttpRule.PatternOneofCase.None)
                {
                    continue;
                }

                var (verb, template) = rule.PatternCase switch
                {
                    HttpRule.PatternOneofCase.Get => ("get", rule.Get),
                    HttpRule.PatternOneofCase.Put => ("put", rule.Put),
                    HttpRule.PatternOneofCase.Post => ("post", rule.Post),
                    HttpRule.PatternOneofCase.Delete => ("delete", rule.Delete),
                    HttpRule.PatternOneofCase.Patch => ("patch", rule.Patch),
                    _ => throw new NotSupportedException($"{method.FullName}: custom HTTP patterns are not supported."),
                };

                var pathParams = ExtractPathParams(template);
                var operation = BuildOperation(binding, method, rule, verb, pathParams, schemas);
                var item = paths[ToOpenApiPath(template)] as JsonObject ?? new JsonObject();
                item[verb] = operation;
                paths[ToOpenApiPath(template)] = item;
            }
        }

        foreach (var extra in extraPaths ?? [])
        {
            foreach (var (path, value) in extra)
            {
                paths[path] = value?.DeepClone();
            }
        }

        schemas["google.rpc.Status"] = StatusSchema();
        return new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = title,
                ["version"] = version,
                ["description"] = "REST/JSON view of the nexopbx.v1 gRPC contract. Generated from the .proto files.",
                ["license"] = new JsonObject { ["name"] = "AGPL-3.0-only", ["identifier"] = "AGPL-3.0-only" },
            },
            ["tags"] = tags,
            ["paths"] = Sorted(paths),
            ["components"] = new JsonObject
            {
                ["securitySchemes"] = new JsonObject
                {
                    [BearerScheme] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer", ["description"] = "Opaque token starting with nxp_ (from Login or CreateApiToken)." },
                },
                ["schemas"] = Sorted(schemas),
            },
        };
    }

    private static JsonObject BuildOperation(
        ServiceBinding binding,
        MethodDescriptor method,
        HttpRule rule,
        string verb,
        IReadOnlyList<string> pathParams,
        JsonObject schemas)
    {
        var operation = new JsonObject
        {
            ["operationId"] = $"{binding.Descriptor.Name}_{method.Name}",
            ["tags"] = new JsonArray(binding.Descriptor.Name),
            ["summary"] = method.Name,
        };

        var parameters = new JsonArray();
        var request = method.InputType;
        foreach (var name in pathParams)
        {
            var field = request.FindFieldByName(name.Split('.')[^1]);
            parameters.Add(new JsonObject
            {
                ["name"] = name,
                ["in"] = "path",
                ["required"] = true,
                ["schema"] = field is null ? new JsonObject { ["type"] = "string" } : FieldSchema(field, schemas, inlineOnly: true),
            });
        }

        JsonObject? body = null;
        if (rule.Body == "*")
        {
            body = MessageSchemaWithout(request, pathParams, schemas);
        }
        else if (!string.IsNullOrEmpty(rule.Body))
        {
            body = FieldSchema(request.FindFieldByName(rule.Body), schemas);
        }
        else if (verb is "get" or "delete")
        {
            foreach (var field in request.Fields.InDeclarationOrder().Where(f => !pathParams.Contains(f.Name) && f.FieldType != FieldType.Message))
            {
                parameters.Add(new JsonObject { ["name"] = field.JsonName, ["in"] = "query", ["schema"] = FieldSchema(field, schemas) });
            }
        }

        if (parameters.Count > 0)
        {
            operation["parameters"] = parameters;
        }

        if (body is not null)
        {
            operation["requestBody"] = new JsonObject
            {
                ["required"] = true,
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = body } },
            };
        }

        var ok = new JsonObject { ["description"] = method.IsServerStreaming ? "A stream of messages (JSON array, delivered incrementally)." : "Success." };
        if (method.OutputType.FullName != "google.protobuf.Empty")
        {
            var schema = new JsonObject { ["$ref"] = MessageRef(method.OutputType, schemas) };
            ok["content"] = new JsonObject
            {
                ["application/json"] = new JsonObject { ["schema"] = method.IsServerStreaming ? new JsonObject { ["type"] = "array", ["items"] = schema } : schema },
            };
        }

        operation["responses"] = new JsonObject
        {
            ["200"] = ok,
            ["default"] = new JsonObject
            {
                ["description"] = "Error. `details` carries a google.rpc.ErrorInfo whose `reason` is a stable machine-readable code.",
                ["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/google.rpc.Status" } },
                },
            },
        };

        ApplySecurity(operation, binding.Implementation, method.Name);
        if (method.IsServerStreaming)
        {
            operation["x-streaming"] = "server";
        }

        return operation;
    }

    private static void ApplySecurity(JsonObject operation, Type implementation, string methodName)
    {
        var impl = implementation.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        var attributes = (impl?.GetCustomAttributes<AuthorizeAttribute>(inherit: true) ?? [])
            .Concat(implementation.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .ToList();
        var anonymous = impl?.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

        if (anonymous || attributes.Count == 0)
        {
            operation["security"] = new JsonArray();
            return;
        }

        operation["security"] = new JsonArray(new JsonObject { [BearerScheme] = new JsonArray() });
        var scopes = attributes.Where(a => !string.IsNullOrEmpty(a.Policy)).Select(a => a.Policy!).Distinct().Order(StringComparer.Ordinal).ToList();
        if (scopes.Count > 0)
        {
            operation["x-required-scopes"] = new JsonArray(scopes.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        }
    }

    private static JsonObject MessageSchemaWithout(MessageDescriptor message, IReadOnlyList<string> excluded, JsonObject schemas)
    {
        var properties = new JsonObject();
        foreach (var field in message.Fields.InDeclarationOrder().Where(f => !excluded.Contains(f.Name)))
        {
            properties[field.JsonName] = FieldSchema(field, schemas);
        }

        return new JsonObject { ["type"] = "object", ["properties"] = properties };
    }

    private static JsonObject FieldSchema(FieldDescriptor? field, JsonObject schemas, bool inlineOnly = false)
    {
        if (field is null)
        {
            return new JsonObject { ["type"] = "object" };
        }

        JsonObject schema;
        if (field.IsMap)
        {
            var value = field.MessageType.FindFieldByName("value");
            schema = new JsonObject { ["type"] = "object", ["additionalProperties"] = ScalarOrRef(value, schemas) };
        }
        else
        {
            schema = ScalarOrRef(field, schemas);
            if (field.IsRepeated)
            {
                schema = new JsonObject { ["type"] = "array", ["items"] = schema };
            }
        }

        return inlineOnly && schema.ContainsKey("$ref") ? new JsonObject { ["type"] = "string" } : schema;
    }

    private static JsonObject ScalarOrRef(FieldDescriptor field, JsonObject schemas) => field.FieldType switch
    {
        FieldType.String => new JsonObject { ["type"] = "string" },
        FieldType.Bool => new JsonObject { ["type"] = "boolean" },
        FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => new JsonObject { ["type"] = "integer", ["format"] = "int32" },
        FieldType.UInt32 or FieldType.Fixed32 => new JsonObject { ["type"] = "integer", ["format"] = "uint32" },
        FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => new JsonObject { ["type"] = "string", ["format"] = "int64" },
        FieldType.UInt64 or FieldType.Fixed64 => new JsonObject { ["type"] = "string", ["format"] = "uint64" },
        FieldType.Float or FieldType.Double => new JsonObject { ["type"] = "number" },
        FieldType.Bytes => new JsonObject { ["type"] = "string", ["format"] = "byte" },
        FieldType.Enum => new JsonObject { ["$ref"] = EnumRef(field.EnumType, schemas) },
        FieldType.Message => WellKnown(field.MessageType) ?? new JsonObject { ["$ref"] = MessageRef(field.MessageType, schemas) },
        _ => new JsonObject { ["type"] = "string" },
    };

    private static JsonObject? WellKnown(MessageDescriptor message) => message.FullName switch
    {
        "google.protobuf.Timestamp" => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
        "google.protobuf.Duration" => new JsonObject { ["type"] = "string", ["description"] = "Seconds with up to 9 fractional digits and an 's' suffix, e.g. \"3.5s\"." },
        "google.protobuf.FieldMask" => new JsonObject { ["type"] = "string", ["description"] = "Comma-separated field paths in lowerCamelCase." },
        "google.protobuf.Empty" => new JsonObject { ["type"] = "object" },
        "google.protobuf.Struct" or "google.protobuf.Any" => new JsonObject { ["type"] = "object" },
        _ => null,
    };

    private static string MessageRef(MessageDescriptor message, JsonObject schemas)
    {
        if (message.FullName == "google.protobuf.Empty")
        {
            return "#/components/schemas/google.protobuf.Empty";
        }

        var name = message.FullName;
        if (!schemas.ContainsKey(name))
        {
            schemas[name] = new JsonObject(); // reserve the slot first: messages may reference themselves
            var properties = new JsonObject();
            foreach (var field in message.Fields.InDeclarationOrder())
            {
                properties[field.JsonName] = FieldSchema(field, schemas);
            }

            schemas[name] = new JsonObject { ["type"] = "object", ["properties"] = properties };
        }

        return $"#/components/schemas/{name}";
    }

    private static string EnumRef(EnumDescriptor enumType, JsonObject schemas)
    {
        var name = enumType.FullName;
        if (!schemas.ContainsKey(name))
        {
            schemas[name] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(enumType.Values.Select(v => (JsonNode)JsonValue.Create(v.Name)!).ToArray()),
            };
        }

        return $"#/components/schemas/{name}";
    }

    private static JsonObject StatusSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["code"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
            ["message"] = new JsonObject { ["type"] = "string" },
            ["details"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object", ["description"] = "An Any: `@type` plus the fields of that type (e.g. google.rpc.ErrorInfo)." } },
        },
    };

    private static List<string> ExtractPathParams(string template)
    {
        var result = new List<string>();
        var start = template.IndexOf('{', StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = template.IndexOf('}', start);
            result.Add(template[(start + 1)..end].Split('=')[0]);
            start = template.IndexOf('{', end);
        }

        return result;
    }

    // gRPC transcoding uses "/v1/auth:login"; OpenAPI paths accept the colon verbatim.
    private static string ToOpenApiPath(string template) => template;

    private static JsonObject Sorted(JsonObject source)
    {
        var sorted = new JsonObject();
        foreach (var key in source.Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList())
        {
            var value = source[key];
            source.Remove(key);
            sorted[key] = value;
        }

        return sorted;
    }
}
