using dbnauta;
using Microsoft.Data.SqlClient;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();

if (command is "--help" or "-h" or "help")
{
    if (args.Length > 1 && args[1].Equals("--agent", StringComparison.OrdinalIgnoreCase))
    {
        PrintAgentHelp();
    }
    else
    {
        PrintUsage();
    }
    return 0;
}

if (command is "--help-agent" or "help-agent" or "agent-help")
{
    PrintAgentHelp();
    return 0;
}

DotNetEnv.Env.TraversePath().Load();

var connectionString = Environment.GetEnvironmentVariable("DBNAUTA_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Connection string not configured. Set 'DBNAUTA_CONNECTION_STRING' in the project's .env file or as an environment variable.");
    return 1;
}

try
{
    switch (command)
    {
        case "query":
        case "sql":
            {
                var sql = args.Length > 1 ? args[1] : Console.In.ReadToEnd();
                if (string.IsNullOrWhiteSpace(sql))
                {
                    Console.Error.WriteLine("Provide the SQL query: dbnauta query \"SELECT ...\"");
                    return 1;
                }
                await RunQueryAsync(connectionString, sql);
                return 0;
            }

        case "tables":
            {
                await RunQueryAsync(connectionString,
                    "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW') ORDER BY TABLE_SCHEMA, TABLE_NAME");
                return 0;
            }

        case "columns":
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Provide the table name: dbnauta columns <table>");
                    return 1;
                }
                var table = args[1];
                await RunQueryAsync(connectionString,
                    "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table ORDER BY ORDINAL_POSITION",
                    parameters: [new SqlParameter("@table", table)]);
                return 0;
            }

        case "top":
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Provide the table name: dbnauta top <table> [n]");
                    return 1;
                }
                var table = FormatTableIdentifier(args[1]);
                var n = 20;
                if (args.Length > 2 && (!int.TryParse(args[2], out n) || n <= 0))
                {
                    Console.Error.WriteLine("The [n] parameter must be a positive integer.");
                    return 1;
                }
                await RunQueryAsync(connectionString, $"SELECT TOP {n} * FROM {table}");
                return 0;
            }

        default:
            PrintUsage();
            return 1;
    }
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        dbnauta - read-only (SELECT) query CLI for SQL Server.

        Usage:
          dbnauta query "<SQL>"        Runs a SELECT query and prints the result as a table.
          dbnauta sql "<SQL>"          Alias for 'query' (also accepts SQL via stdin).
          dbnauta tables               Lists the tables and views in the database.
          dbnauta columns <table>      Lists the columns, types and nullability of a table.
          dbnauta top <table> [n]      Shows the first n rows (default: 20) of a table.
          dbnauta --help | -h | help   Shows this help for human users.
          dbnauta --help-agent         Shows a structured guide for AI agents.

        Examples:
          dbnauta tables
          dbnauta columns dbo.Users
          dbnauta top Users 10
          dbnauta query "SELECT TOP 5 Id, Name FROM Users ORDER BY Id DESC"

        Security (read-only):
          - Only SELECT queries (and WITH ... SELECT CTEs) are allowed.
          - Write/DDL/execution commands (INSERT, UPDATE, DELETE, MERGE, DROP, ALTER,
            CREATE, TRUNCATE, EXEC, SELECT INTO, GRANT, etc.) and multiple statements with ';'
            are blocked before reaching the database.

        Configuration:
          Set 'DBNAUTA_CONNECTION_STRING' in the project's .env file (searched in parent directories)
          or as a system environment variable.
        """);
}

static void PrintAgentHelp()
{
    Console.WriteLine("""
        # dbnauta - Guide for AI Agents

        ## Purpose
        Read-only CLI for safely querying SQL Server databases.

        ## Recommended discovery flow
        1. `dbnauta tables` -> discovers existing schemas, tables and views.
        2. `dbnauta columns <table>` -> inspects columns, data types and nullability.
        3. `dbnauta top <table> [n]` -> quick data sample (default n=20).
        4. `dbnauta query "<SQL>"` (or via stdin: `echo "SELECT ..." | dbnauta query`) -> specific query.

        ## Security Rules (mandatory)
        - ONLY `SELECT` statements (or `WITH ... SELECT`) are accepted.
        - Forbidden: `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `DROP`, `ALTER`, `CREATE`, `TRUNCATE`,
          `EXEC`/`EXECUTE`, `SELECT ... INTO`, `GRANT`/`REVOKE`/`DENY`, transactions (`BEGIN`/`COMMIT`/`ROLLBACK`),
          `SET`, `DECLARE`, `USE`, `DBCC`, `OPENROWSET` and multiple statements separated by `;`.
        - A single `;` is allowed only at the end of the query.

        ## Output Format and Exit Codes
        - Standard output (stdout): table aligned with `|`, followed by `(<N> row(s))`. Null values appear as `NULL`.
        - Errors and security blocks (stderr): descriptive message.
        - Exit code `0`: success. Exit code `1`: argument error, security block, connection failure or invalid SQL.

        ## Tips for Agents
        - Prefer `TOP` or `WHERE` filters on large tables to avoid huge outputs.
        - For complex queries or queries with many quotes, send the SQL via stdin (`dbnauta query` without a second argument).
        - Requires the `DBNAUTA_CONNECTION_STRING` variable (via the project's `.env` or the environment).
        """);
}

static string FormatTableIdentifier(string rawTable)
{
    var parts = rawTable.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length is 0 or > 2)
    {
        throw new ArgumentException($"Invalid table name: '{rawTable}'. Use 'table' or 'schema.table'.");
    }

    return string.Join(".", parts.Select(p => $"[{p.Replace("]", "]]")}]"));
}

static async Task RunQueryAsync(
    string connectionString,
    string sql,
    int timeoutSeconds = 60,
    SqlParameter[]? parameters = null)
{
    SqlQueryValidator.EnsureSelectOnly(sql);

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    connection.InfoMessage += (_, e) => Console.WriteLine(e.Message);

    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.CommandTimeout = timeoutSeconds;
    if (parameters is { Length: > 0 })
    {
        command.Parameters.AddRange(parameters);
    }

    await using var reader = await command.ExecuteReaderAsync();

    var hadResult = false;
    do
    {
        if (reader.FieldCount == 0)
        {
            continue;
        }

        if (hadResult)
        {
            Console.WriteLine();
        }
        hadResult = true;

        var columnNames = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();

        var rows = new List<string[]>();
        while (await reader.ReadAsync())
        {
            var row = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                row[i] = value is DBNull ? "NULL" : value.ToString() ?? string.Empty;
            }
            rows.Add(row);
        }

        PrintTable(columnNames, rows);
        Console.WriteLine($"({rows.Count} row(s))");
    }
    while (await reader.NextResultAsync());

    if (!hadResult)
    {
        Console.WriteLine($"OK. Rows affected: {reader.RecordsAffected}");
    }
}

static void PrintTable(string[] columns, List<string[]> rows)
{
    var widths = columns.Select((c, i) => Math.Max(c.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();

    void PrintRow(string[] values)
    {
        Console.WriteLine(string.Join(" | ", values.Select((v, i) => v.PadRight(widths[i]))));
    }

    PrintRow(columns);
    Console.WriteLine(string.Join("-+-", widths.Select(w => new string('-', w))));
    foreach (var row in rows)
    {
        PrintRow(row);
    }
}
