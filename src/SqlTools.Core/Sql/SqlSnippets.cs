using System.Collections.Generic;

namespace SqlTools.Sql
{
    public sealed class SqlSnippet
    {
        public string Shortcut { get; set; }
        public string Text { get; set; }
        public string Description { get; set; }
        /// <summary>Faz sentido no meio de uma instrução (ex.: ij → INNER JOIN), além do começo.</summary>
        public bool InStatement { get; set; }
    }

    /// <summary>Atalhos digitáveis: ssf + Tab → SELECT * FROM.</summary>
    public static class SqlSnippets
    {
        public static readonly IReadOnlyList<SqlSnippet> BuiltIn = new List<SqlSnippet>
        {
            S("ssf", "SELECT * FROM", "SELECT * FROM"),
            S("st", "SELECT TOP 100 * FROM", "SELECT TOP 100 * FROM"),
            S("scf", "SELECT COUNT(*) FROM", "SELECT COUNT(*) FROM"),
            S("sdf", "SELECT DISTINCT", "SELECT DISTINCT"),
            S("ii", "INSERT INTO", "INSERT INTO"),
            S("uu", "UPDATE", "UPDATE"),
            S("df", "DELETE FROM", "DELETE FROM"),
            S("bt", "BEGIN TRAN;", "BEGIN TRAN"),
            S("ct", "COMMIT TRAN;", "COMMIT TRAN"),
            S("rt", "ROLLBACK TRAN;", "ROLLBACK TRAN"),
            S("btc", "BEGIN TRY\r\n    \r\nEND TRY\r\nBEGIN CATCH\r\n    THROW;\r\nEND CATCH", "BEGIN TRY ... END TRY / BEGIN CATCH THROW END CATCH"),
            S("bte", "BEGIN TRAN;\r\nBEGIN TRY\r\n    \r\n    COMMIT TRAN;\r\nEND TRY\r\nBEGIN CATCH\r\n    IF @@TRANCOUNT > 0 ROLLBACK TRAN;\r\n    THROW;\r\nEND CATCH", "Transação com TRY/CATCH e ROLLBACK"),
            S("cte", "WITH cte AS (\r\n    SELECT \r\n)\r\nSELECT * FROM cte", "WITH cte AS (...) SELECT"),
            S("ifex", "IF EXISTS (SELECT 1 FROM )", "IF EXISTS (SELECT 1 FROM ...)"),
            S("dv", "DECLARE @", "DECLARE @variável"),
            S("ctt", "CREATE TABLE #tmp (\r\n    Id int\r\n);", "CREATE TABLE #tmp"),
            S("snc", "SET NOCOUNT ON;", "SET NOCOUNT ON"),
            S("swn", "SELECT * FROM sys.dm_exec_requests WHERE session_id <> @@SPID;", "Requisições em execução"),
            S("ij", "INNER JOIN", "INNER JOIN", true),
            S("lj", "LEFT JOIN", "LEFT JOIN", true),
            S("rj", "RIGHT JOIN", "RIGHT JOIN", true),
            S("ca", "CROSS APPLY", "CROSS APPLY", true),
            S("oa", "OUTER APPLY", "OUTER APPLY", true),
            S("ob", "ORDER BY", "ORDER BY", true),
            S("gb", "GROUP BY", "GROUP BY", true),
            S("wnl", "WITH (NOLOCK)", "WITH (NOLOCK)", true),
            S("isn", "IS NULL", "IS NULL", true),
            S("inn", "IS NOT NULL", "IS NOT NULL", true),
        };

        private static SqlSnippet S(string shortcut, string text, string description, bool inStatement = false) =>
            new SqlSnippet { Shortcut = shortcut, Text = text, Description = description, InStatement = inStatement };
    }
}
