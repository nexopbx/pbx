// SPDX-License-Identifier: AGPL-3.0-only
using System.CommandLine;
using NexoPBX.App;

// nexopbxd: the single NexoPBX application. With no subcommand it runs the server in the foreground.

var configOption = new Option<string?>("--config") { Description = "Path of the YAML configuration file.", Recursive = true };
var dataDirOption = new Option<string?>("--data-dir") { Description = "Directory for the database, keys and runtime files.", Recursive = true };

var root = new RootCommand("NexoPBX server and administration tool.");
root.Options.Add(configOption);
root.Options.Add(dataDirOption);

NexoPaths PathsFor(ParseResult result, bool serviceMode) =>
    NexoPaths.Resolve(serviceMode, result.GetValue(configOption), result.GetValue(dataDirOption));

root.SetAction((result, ct) => Cli.RunServerAsync(PathsFor(result, false), serviceMode: false, ct));

var run = new Command("run", "Run the server in the foreground (headless).");
run.SetAction((result, ct) => Cli.RunServerAsync(PathsFor(result, false), serviceMode: false, ct));
root.Subcommands.Add(run);

var service = new Command("service", "Run as a Windows Service or systemd unit (uses system locations for configuration and data).");
service.SetAction((result, ct) => Cli.RunServerAsync(PathsFor(result, true), serviceMode: true, ct));
root.Subcommands.Add(service);

var db = new Command("db", "Database administration.");
var migrate = new Command("migrate", "Apply pending database migrations.");
migrate.SetAction((result, ct) => Cli.DbMigrateAsync(PathsFor(result, false), ct));
var status = new Command("status", "Show whether migrations are pending.");
status.SetAction((result, ct) => Cli.DbStatusAsync(PathsFor(result, false), ct));
db.Subcommands.Add(migrate);
db.Subcommands.Add(status);
root.Subcommands.Add(db);

var user = new Command("user", "User administration.");
var usernameOption = new Option<string>("--username") { Description = "Login name.", Required = true };
var displayNameOption = new Option<string?>("--display-name") { Description = "Name shown in the UI (default: the username)." };
var passwordStdin = new Option<bool>("--password-stdin") { Description = "Read the password from the first line of standard input." };
var bootstrap = new Command("bootstrap", "Create the first Owner of a fresh installation. Refused once any user exists.");
bootstrap.Options.Add(usernameOption);
bootstrap.Options.Add(displayNameOption);
bootstrap.Options.Add(passwordStdin);
bootstrap.SetAction((result, ct) => Cli.UserBootstrapAsync(
    PathsFor(result, false),
    result.GetValue(usernameOption)!,
    result.GetValue(displayNameOption),
    result.GetValue(passwordStdin),
    ct));
user.Subcommands.Add(bootstrap);
root.Subcommands.Add(user);

var token = new Command("token", "API token administration.");
var tokenUser = new Option<string>("--user") { Description = "Username the token acts as.", Required = true };
var tokenName = new Option<string>("--name") { Description = "Label for the token.", Required = true };
var tokenScopes = new Option<string[]>("--scope") { Description = "A scope to grant; repeat for several (e.g. --scope system.read --scope events.subscribe).", Required = true, AllowMultipleArgumentsPerToken = true };
var tokenDays = new Option<int>("--days") { Description = "Lifetime in days; 0 (default) means it never expires." };
var tokenCreate = new Command("create", "Create an API token and print it once.");
tokenCreate.Options.Add(tokenUser);
tokenCreate.Options.Add(tokenName);
tokenCreate.Options.Add(tokenScopes);
tokenCreate.Options.Add(tokenDays);
tokenCreate.SetAction((result, ct) => Cli.TokenCreateAsync(
    PathsFor(result, false),
    result.GetValue(tokenUser)!,
    result.GetValue(tokenName)!,
    result.GetValue(tokenScopes)!,
    result.GetValue(tokenDays),
    ct));
token.Subcommands.Add(tokenCreate);
root.Subcommands.Add(token);

var version = new Command("version", "Print the version.");
version.SetAction(_ => Cli.PrintVersion());
root.Subcommands.Add(version);

return await root.Parse(args).InvokeAsync();
