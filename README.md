# dbnauta

A small CLI to query SQL Server in read-only mode. Only `SELECT` queries (and `WITH ... SELECT`) get through, everything else is blocked before it reaches the database. I built it so AI agents can poke around a database without being able to change anything.

## Install

Requires the .NET 10 SDK.

```powershell
dotnet pack -c Release
dotnet tool install --global --add-source ./bin/Release dbnauta
```

## Setup

Set the connection string in a `.env` file (searched in the current and parent directories) or as an environment variable:

```
DBNAUTA_CONNECTION_STRING=Server=localhost;Database=MyDb;User Id=reader;Password=...;TrustServerCertificate=True
```

Use a database user with read-only permissions. The query validator is a safety net, not a replacement for proper permissions.

## Usage

```powershell
dbnauta tables                      # list tables and views
dbnauta columns dbo.Users           # columns, types, nullability
dbnauta top dbo.Users 10            # first 10 rows (default 20)
dbnauta query "SELECT TOP 5 * FROM Users"
"SELECT 1" | dbnauta query          # SQL via stdin
dbnauta --help-agent                # quick guide for AI agents
```

Exit code is `0` on success and `1` on any error or blocked query.

## What gets blocked

`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `SELECT ... INTO`, DDL (`CREATE`, `ALTER`, `DROP`, `TRUNCATE`), `EXEC`, `OPENROWSET` and friends, `GRANT`/`REVOKE`/`DENY`, transactions, `SET`, `DECLARE`, `USE`, and multiple statements separated by `;`.

## AI agent skill

[SKILL.md](SKILL.md) is a ready-to-use skill file that teaches an agent how to use the tool.
