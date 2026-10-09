// SPDX-License-Identifier: AGPL-3.0-only
using System.Xml.Linq;

namespace NexoPBX.Architecture.Tests;

/// <summary>
/// Enforces ARCHITECTURE §4.1 mechanically. Adding a project or a reference that is not declared here fails the build,
/// which forces the change to be a conscious decision (and an ADR when it bends a rule).
/// </summary>
public sealed class DependencyRulesTests
{
    // project -> the only projects it may reference directly
    private static readonly Dictionary<string, string[]> AllowedProjectReferences = new(StringComparer.Ordinal)
    {
        ["NexoPBX.Abstractions"] = [],
        ["NexoPBX.Contracts"] = [],
        ["NexoPBX.Testing"] = ["NexoPBX.Abstractions"],
        ["NexoPBX.Store"] = ["NexoPBX.Abstractions"],
        ["NexoPBX.Store.Sqlite"] = ["NexoPBX.Store"],
        ["NexoPBX.Store.Postgres"] = ["NexoPBX.Store"],
        ["NexoPBX.Auth"] = ["NexoPBX.Abstractions", "NexoPBX.Store"],
        ["NexoPBX.Api"] = ["NexoPBX.Abstractions", "NexoPBX.Contracts", "NexoPBX.Auth", "NexoPBX.Store"],
        ["NexoPBX.App"] = ["NexoPBX.Abstractions", "NexoPBX.Contracts", "NexoPBX.Store", "NexoPBX.Store.Sqlite", "NexoPBX.Store.Postgres", "NexoPBX.Auth", "NexoPBX.Api"],

        // Clients of the public API: they may see the contract and nothing of the server (ADR-004, "no back doors").
        ["NexoPBX.SampleClient"] = ["NexoPBX.Contracts"],
        ["NexoPBX.Desktop"] = ["NexoPBX.Contracts"],

        // Pure logic modules: no I/O, so no reference to networking, storage or the web stack (ADR-005).
        ["NexoPBX.Flow"] = ["NexoPBX.Abstractions"],
        ["NexoPBX.Queue"] = ["NexoPBX.Abstractions"],
    };

    // packages a project must never take, however it got there
    private static readonly Dictionary<string, string[]> ForbiddenPackagePrefixes = new(StringComparer.Ordinal)
    {
        ["NexoPBX.Abstractions"] = ["Microsoft.EntityFrameworkCore", "Grpc", "Google.Protobuf", "Npgsql", "Serilog"],
        ["NexoPBX.Contracts"] = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql"],
        ["NexoPBX.Flow"] = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Grpc", "Npgsql"],
        ["NexoPBX.Queue"] = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Grpc", "Npgsql"],
        ["NexoPBX.SampleClient"] = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore"],
        ["NexoPBX.Desktop"] = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore"],
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexoPBX.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("NexoPBX.slnx not found above the test binaries.");
    }

    private static IEnumerable<string> ProductProjects() =>
        new[] { "src", "samples" }
            .Select(d => Path.Combine(RepoRoot(), d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.csproj", SearchOption.AllDirectories));

    private static string Name(string csproj) => Path.GetFileNameWithoutExtension(csproj);

    [Fact]
    public void Every_product_project_is_declared_in_the_rules()
    {
        var undeclared = ProductProjects().Select(Name).Where(n => !AllowedProjectReferences.ContainsKey(n)).ToList();

        Assert.True(undeclared.Count == 0, $"Declare these projects in DependencyRulesTests: {string.Join(", ", undeclared)}");
    }

    [Fact]
    public void Projects_only_reference_what_the_architecture_allows()
    {
        var violations = new List<string>();
        foreach (var csproj in ProductProjects())
        {
            var name = Name(csproj);
            if (!AllowedProjectReferences.TryGetValue(name, out var allowed))
            {
                continue;
            }

            var references = XDocument.Load(csproj).Descendants("ProjectReference")
                .Select(e => Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/')));
            violations.AddRange(references.Where(r => !allowed.Contains(r)).Select(r => $"{name} -> {r}"));
        }

        Assert.True(violations.Count == 0, "Forbidden project references:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void Layered_projects_do_not_pull_in_forbidden_packages()
    {
        var violations = new List<string>();
        foreach (var csproj in ProductProjects())
        {
            var name = Name(csproj);
            if (!ForbiddenPackagePrefixes.TryGetValue(name, out var forbidden))
            {
                continue;
            }

            var packages = XDocument.Load(csproj).Descendants("PackageReference").Select(e => (string)e.Attribute("Include")!);
            violations.AddRange(packages.Where(p => forbidden.Any(f => p.StartsWith(f, StringComparison.Ordinal))).Select(p => $"{name} uses {p}"));
        }

        Assert.True(violations.Count == 0, "Forbidden packages:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void The_solution_lists_every_project_on_disk()
    {
        var root = RepoRoot();
        var solution = File.ReadAllText(Path.Combine(root, "NexoPBX.slnx"));
        var missing = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Where(rel => !solution.Contains(rel, StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0, $"Not in NexoPBX.slnx: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Hand_written_source_files_carry_the_spdx_license_header()
    {
        var root = RepoRoot();
        var missing = new[] { "src", "tests", "samples" }
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !File.ReadLines(p).First().StartsWith("// SPDX-License-Identifier: AGPL-3.0-only", StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(root, p))
            .ToList();

        Assert.True(missing.Count == 0, "Missing SPDX header:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void All_public_protos_live_in_the_v1_package()
    {
        var protoDir = Path.Combine(RepoRoot(), "src", "NexoPBX.Contracts", "Protos", "nexopbx", "v1");
        foreach (var proto in Directory.GetFiles(protoDir, "*.proto"))
        {
            Assert.Contains("package nexopbx.v1;", File.ReadAllText(proto), StringComparison.Ordinal);
        }
    }
}
