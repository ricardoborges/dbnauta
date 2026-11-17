---
name: dbnauta
description: Query SQL Server databases safely in read-only mode (SELECT only) using the dbnauta CLI. Use when you need to inspect tables, columns, data samples, or run SELECT queries for diagnosis and analysis.
---

# Skill: dbnauta

Use the `dbnauta` CLI to explore schemas, inspect data, and run **read-only (`SELECT`)** SQL queries against SQL Server databases.

## Prerequisites
- The `DBNAUTA_CONNECTION_STRING` variable must be set (in the current project's `.env` file or in the system environment).
- To see the built-in quick guide for agents at any time, run:
  ```powershell
  dbnauta --help-agent
  ```

## Recommended Workflow for AI Agents

1. **Discover the available tables and views:**
   ```powershell
   dbnauta tables
   ```
2. **Inspect the columns of a specific table:**
   ```powershell
   dbnauta columns <table>
   ```
3. **Look at a quick sample of rows (default 20):**
   ```powershell
   dbnauta top <table> 10
   ```
4. **Run a specific `SELECT` query:**
   ```powershell
   dbnauta query "SELECT TOP 20 Id, Name FROM dbo.Users WHERE Active = 1 ORDER BY Id DESC"
   ```
   For long queries or queries with many quotes, send them via `stdin`:
   ```powershell
   @'
   SELECT TOP 20 u.Id, u.Name
   FROM dbo.Users u
   WHERE u.Active = 1
   '@ | dbnauta query
   ```

## Security Restrictions (Read-Only)
`dbnauta` validates every query locally before sending it to SQL Server:
- **Allowed:** only `SELECT` statements and CTEs (`WITH ... SELECT`), including an optional trailing `;`.
- **Blocked:**
  - DML/write commands: `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `UPSERT`, `SELECT ... INTO`.
  - DDL commands: `CREATE`, `ALTER`, `DROP`, `TRUNCATE`, `RENAME`.
  - Execution/external access: `EXEC`, `EXECUTE`, `sp_executesql`, `xp_cmdshell`, `OPENROWSET`, `OPENDATASOURCE`, `OPENQUERY`.
  - Control/administration/transactions: `GRANT`, `REVOKE`, `DENY`, `BEGIN`, `COMMIT`, `ROLLBACK`, `SET`, `DECLARE`, `USE`, `DBCC`, `BACKUP`, `RESTORE`, `BULK`, `KILL`, `WAITFOR`.
  - Multiple statements separated by `;` (e.g. `SELECT 1; DROP TABLE ...`).

## Output Format and Exit Codes
- **stdout:** table aligned with a `|` separator, followed by the row count `(<N> row(s))`. `DBNull` values are shown as `NULL`.
- **stderr:** validation, security, or connection error messages.
- **Exit code `0`:** query or help command ran successfully.
- **Exit code `1`:** invalid arguments, security block, missing `DBNAUTA_CONNECTION_STRING`, or SQL/connection error.

## Best Practices
- Always limit the amount of data returned with `TOP` or `WHERE` filters before querying unfamiliar tables.
- Use `dbnauta columns <table>` before writing a `SELECT` to avoid column-name errors.
- Never try to work around the security restrictions; `dbnauta` is designed strictly for reading.
