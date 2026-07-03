using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.EntityFramework;

public class EfMigrationResourceArgs
{
    /// <summary>
    /// Configuration for the DbContext to be migrated.
    /// </summary>
    public EfContextSettings Context { get; set; } = null!;

    /// <summary>
    /// A value that changes when migrations change (e.g. latest migration ID or hash).
    /// This ensures Pulumi shows an update when migrations are pending.
    /// </summary>
    public Input<string> MigrationTrigger { get; set; } = null!;

    /// <summary>
    /// Working directory for executing commands.
    /// </summary>
    public string? WorkingDirectory { get; set; }
    
    /// <summary>
    /// Environment variables needed for DB connection.
    /// </summary>
    public InputMap<string> EnvironmentVariables { get; set; } = new InputMap<string>();
    
    /// <summary>
    /// Dependency on the database being ready.
    /// </summary>
    public Input<string> DbHost { get; set; } = null!;
}

public class EfMigrationResource : ComponentResource
{
    [Output]
    public Output<string> Status { get; private set; }

    public EfMigrationResource(string name, EfMigrationResourceArgs args, ComponentResourceOptions? options = null)
        : base("crosscloud:ef:Migration", name, options)
    {
        // We use Output.Tuple to wait for all inputs to be resolved before running migrations
        Status = Output.Tuple(args.DbHost, args.EnvironmentVariables, args.MigrationTrigger).Apply(async t =>
        {
            var host = t.Item1;
            var envVars = t.Item2;
            var trigger = t.Item3;

            var isDryRun = Deployment.Instance.IsDryRun;

            Log.Debug($"Database host {host} is ready. Checking migration for {args.Context.ContextName} (DryRun: {isDryRun})...");

            var serviceOptions = new EfMigrationServiceOptions
            {
                Contexts = [args.Context],
                WorkingDirectory = args.WorkingDirectory,
                EnvironmentVariables = envVars
            };

            var service = new EfMigrationService(serviceOptions);
            
            // If it's a dry run, we only check for updates but don't apply them
            // However, if the host is a placeholder (unknown), we can't even check.
            if (isDryRun && host.Contains("unknown"))
            {
                return $"Pending: {args.Context.ContextName} migration will be checked when DB host is available.";
            }

            await service.RunMigrationsAsync(apply: !isDryRun);

            return isDryRun ? $"Preview: {args.Context.ContextName} migration checked." : $"Success: {args.Context.ContextName} migration applied.";
        });

        // Ensure the resource reflects the migration status and trigger
        this.RegisterOutputs(new Dictionary<string, object?>
        {
            { "status", Status },
            { "migrationTrigger", args.MigrationTrigger }
        });
    }
}