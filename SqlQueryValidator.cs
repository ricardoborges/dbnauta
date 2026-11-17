namespace dbnauta;

/// <summary>
/// Validates SQL queries to ensure that only read-only SELECT statements are executed.
/// </summary>
public static class SqlQueryValidator
{
    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // DML / writes
        "INSERT", "UPDATE", "DELETE", "MERGE", "UPSERT", "INTO", "WRITETEXT", "UPDATETEXT",
        // DDL
        "CREATE", "ALTER", "DROP", "TRUNCATE", "RENAME",
        // Execution / external access
        "EXEC", "EXECUTE", "SP_EXECUTESQL", "XP_CMDSHELL", "OPENROWSET", "OPENDATASOURCE", "OPENQUERY", "OPENXML",
        // DCL / permissions
        "GRANT", "REVOKE", "DENY",
        // Administration, transactions and control flow
        "BACKUP", "RESTORE", "BULK", "DBCC", "SHUTDOWN", "RECONFIGURE", "KILL",
        "WAITFOR", "CHECKPOINT", "BEGIN", "COMMIT", "ROLLBACK", "SAVE",
        "USE", "GO", "SET", "DECLARE", "PRINT", "RAISERROR", "THROW",
        "ENABLE", "DISABLE"
    };

    private enum TokenKind
    {
        Word,
        StringLiteral,
        QuotedIdentifier,
        Semicolon,
        OpenParen,
        Dot,
        Other
    }

    private readonly record struct Token(TokenKind Kind, string Value);

    public static void EnsureSelectOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("The SQL query cannot be empty.");
        }

        var tokens = Tokenize(sql);

        // Allow a trailing semicolon only; block multiple statements separated by ';'
        var seenSemicolon = false;
        var meaningfulTokens = new List<Token>(tokens.Count);
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Semicolon)
            {
                seenSemicolon = true;
                continue;
            }

            if (seenSemicolon)
            {
                throw new InvalidOperationException(
                    "Operation blocked for security reasons: only one SELECT query at a time is allowed (multiple statements separated by ';' are not permitted).");
            }

            meaningfulTokens.Add(token);
        }

        if (meaningfulTokens.Count == 0)
        {
            throw new ArgumentException("The SQL query contains no executable statements.");
        }

        // The first relevant token (ignoring leading parentheses) must be SELECT or WITH
        var firstIndex = 0;
        while (firstIndex < meaningfulTokens.Count && meaningfulTokens[firstIndex].Kind == TokenKind.OpenParen)
        {
            firstIndex++;
        }

        if (firstIndex >= meaningfulTokens.Count ||
            meaningfulTokens[firstIndex].Kind != TokenKind.Word ||
            (!meaningfulTokens[firstIndex].Value.Equals("SELECT", StringComparison.OrdinalIgnoreCase) &&
             !meaningfulTokens[firstIndex].Value.Equals("WITH", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Operation blocked for security reasons: only SELECT queries are allowed.");
        }

        // Check every keyword outside strings, comments and delimited identifiers
        for (var i = 0; i < meaningfulTokens.Count; i++)
        {
            var token = meaningfulTokens[i];
            if (token.Kind != TokenKind.Word)
            {
                continue;
            }

            // A qualified identifier after a dot (e.g. t.status, dbo.delete_flag) is not a command keyword
            if (i > 0 && meaningfulTokens[i - 1].Kind == TokenKind.Dot)
            {
                continue;
            }

            if (ForbiddenKeywords.Contains(token.Value))
            {
                throw new InvalidOperationException(
                    $"Operation blocked for security reasons: the keyword '{token.Value.ToUpperInvariant()}' is not allowed. Only SELECT queries are accepted.");
            }
        }
    }

    private static List<Token> Tokenize(string sql)
    {
        var tokens = new List<Token>();
        var i = 0;
        var length = sql.Length;

        while (i < length)
        {
            var ch = sql[i];

            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            // Line comment: -- ...
            if (ch == '-' && i + 1 < length && sql[i + 1] == '-')
            {
                i += 2;
                while (i < length && sql[i] != '\n')
                {
                    i++;
                }
                continue;
            }

            // Block comment (T-SQL supports nesting): /* ... */
            if (ch == '/' && i + 1 < length && sql[i + 1] == '*')
            {
                i += 2;
                var depth = 1;
                while (i < length && depth > 0)
                {
                    if (sql[i] == '/' && i + 1 < length && sql[i + 1] == '*')
                    {
                        depth++;
                        i += 2;
                    }
                    else if (sql[i] == '*' && i + 1 < length && sql[i + 1] == '/')
                    {
                        depth--;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }

                if (depth > 0)
                {
                    throw new ArgumentException("Invalid SQL query: unclosed block comment '/*'.");
                }
                continue;
            }

            // String literal: '...' or N'...'
            if (ch == '\'' || ((ch == 'N' || ch == 'n') && i + 1 < length && sql[i + 1] == '\''))
            {
                if (ch != '\'')
                {
                    i++;
                }
                i++; // skip the opening quote
                var closed = false;
                while (i < length)
                {
                    if (sql[i] == '\'')
                    {
                        if (i + 1 < length && sql[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        closed = true;
                        break;
                    }
                    i++;
                }

                if (!closed)
                {
                    throw new ArgumentException("Invalid SQL query: unclosed string literal.");
                }

                tokens.Add(new Token(TokenKind.StringLiteral, string.Empty));
                continue;
            }

            // Bracketed identifier: [...]
            if (ch == '[')
            {
                i++;
                var closed = false;
                while (i < length)
                {
                    if (sql[i] == ']')
                    {
                        if (i + 1 < length && sql[i + 1] == ']')
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        closed = true;
                        break;
                    }
                    i++;
                }

                if (!closed)
                {
                    throw new ArgumentException("Invalid SQL query: unclosed identifier '['.");
                }

                tokens.Add(new Token(TokenKind.QuotedIdentifier, string.Empty));
                continue;
            }

            // Double-quoted identifier: "..."
            if (ch == '"')
            {
                i++;
                var closed = false;
                while (i < length)
                {
                    if (sql[i] == '"')
                    {
                        if (i + 1 < length && sql[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        closed = true;
                        break;
                    }
                    i++;
                }

                if (!closed)
                {
                    throw new ArgumentException("Invalid SQL query: unclosed identifier '\"'.");
                }

                tokens.Add(new Token(TokenKind.QuotedIdentifier, string.Empty));
                continue;
            }

            if (ch == ';')
            {
                tokens.Add(new Token(TokenKind.Semicolon, ";"));
                i++;
                continue;
            }

            if (ch == '(')
            {
                tokens.Add(new Token(TokenKind.OpenParen, "("));
                i++;
                continue;
            }

            if (ch == '.')
            {
                tokens.Add(new Token(TokenKind.Dot, "."));
                i++;
                continue;
            }

            if (IsWordStart(ch))
            {
                var start = i;
                i++;
                while (i < length && IsWordPart(sql[i]))
                {
                    i++;
                }
                tokens.Add(new Token(TokenKind.Word, sql[start..i]));
                continue;
            }

            tokens.Add(new Token(TokenKind.Other, ch.ToString()));
            i++;
        }

        return tokens;
    }

    private static bool IsWordStart(char ch) => char.IsLetter(ch) || ch == '_' || ch == '@' || ch == '#';
    private static bool IsWordPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_' || ch == '@' || ch == '#' || ch == '$';
}
