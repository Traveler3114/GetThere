using Microsoft.Data.SqlClient;

namespace GetThere.Tests;

/// <summary>
/// Resolves the SQL Server the database-backed fixtures run against.
/// <para>
/// The connection string used to be a per-fixture constant pinned to
/// <c>Server=localhost;Trusted_Connection=True</c>. That works on a developer's Windows machine and
/// nowhere else: the CI runner is Linux, has no local SQL Server and cannot do Windows
/// authentication, so every fixture constructor threw and took the whole test job — and with it the
/// build job's green tick — down with it.
/// </para>
/// <para>
/// The base connection string now comes from <c>TESTS_SQL_CONNECTION</c> when it is set (CI points
/// it at a service container), and falls back to the original local default so nothing changes for
/// a developer running the suite on Windows. Each fixture still gets its own database: the name is
/// substituted into whatever base string was supplied, so the two fixtures never share state.
/// </para>
/// </summary>
public static class TestDatabase
{
    /// <summary>Environment variable holding the base connection string. Any database name in it is replaced.</summary>
    public const string ConnectionVariable = "TESTS_SQL_CONNECTION";

    private const string LocalDefault =
        "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>Builds a connection string for an isolated database with the given name.</summary>
    public static string ConnectionStringFor(string databaseName)
    {
        var baseConnection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(baseConnection))
            baseConnection = LocalDefault;

        return new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = databaseName
        }.ConnectionString;
    }
}
