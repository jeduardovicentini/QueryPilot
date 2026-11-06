using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlTools.Sql
{
    public sealed class SqlSnippet
    {
        public string Shortcut { get; set; }
        /// <summary>Texto inserido (sem o marcador $CURSOR$).</summary>
        public string Text { get; set; }
        public string Description { get; set; }
        /// <summary>Faz sentido no meio de uma instrução (ex.: ij → INNER JOIN), além do começo.</summary>
        public bool InStatement { get; set; }
        /// <summary>Onde o cursor fica em <see cref="Text"/> (null = no fim).</summary>
        public int? CursorOffset { get; set; }
        /// <summary>Veio do arquivo do usuário.</summary>
        public bool IsUser { get; set; }
    }

    /// <summary>Atalhos digitáveis: ssf + Tab → SELECT * FROM. Os do usuário vêm de um arquivo (<see cref="Parse"/>).</summary>
    public static class SqlSnippets
    {
        public const string CursorMarker = "$CURSOR$";

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
            S("btc", "BEGIN TRY\r\n    $CURSOR$\r\nEND TRY\r\nBEGIN CATCH\r\n    THROW;\r\nEND CATCH", "BEGIN TRY ... END TRY / BEGIN CATCH THROW END CATCH"),
            S("bte", "BEGIN TRAN;\r\nBEGIN TRY\r\n    $CURSOR$\r\n    COMMIT TRAN;\r\nEND TRY\r\nBEGIN CATCH\r\n    IF @@TRANCOUNT > 0 ROLLBACK TRAN;\r\n    THROW;\r\nEND CATCH", "Transação com TRY/CATCH e ROLLBACK"),
            S("cte", "WITH cte AS (\r\n    SELECT $CURSOR$\r\n)\r\nSELECT * FROM cte", "WITH cte AS (...) SELECT"),
            S("ifex", "IF EXISTS (SELECT 1 FROM $CURSOR$)", "IF EXISTS (SELECT 1 FROM ...)"),
            S("dv", "DECLARE @", "DECLARE @variável"),
            S("ctt", "CREATE TABLE #tmp (\r\n    Id int$CURSOR$\r\n);", "CREATE TABLE #tmp"),
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

        private static SqlSnippet S(string shortcut, string text, string description, bool inStatement = false)
        {
            var s = Make(shortcut, text, description);
            s.InStatement = inStatement;
            return s;
        }

        /// <summary>Snippet a partir do texto com o marcador $CURSOR$ (o primeiro vale; os demais são removidos).</summary>
        public static SqlSnippet Make(string shortcut, string rawText, string description)
        {
            var idx = rawText.IndexOf(CursorMarker, StringComparison.OrdinalIgnoreCase);
            var text = idx < 0 ? rawText : Regex.Replace(rawText, Regex.Escape(CursorMarker), "", RegexOptions.IgnoreCase);
            return new SqlSnippet { Shortcut = shortcut, Text = text, Description = description, CursorOffset = idx < 0 ? (int?)null : idx };
        }

        private static readonly Regex Header = new Regex(@"^\s*--#(\S*)[ \t]*(.*)$", RegexOptions.Compiled);
        private static readonly Regex ValidShortcut = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        /// <summary>
        /// Lê o arquivo do usuário: cada snippet começa com <c>--#atalho descrição</c> e vai até o próximo <c>--#</c>.
        /// O que vem antes do primeiro é ignorado (explicações). Atalho repetido: vale o último.
        /// </summary>
        public static List<SqlSnippet> Parse(string content, List<string> errors = null)
        {
            var result = new List<SqlSnippet>();
            if (string.IsNullOrEmpty(content)) return result;
            var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            string shortcut = null, description = null;
            int headerLine = 0;
            var body = new List<string>();

            void Flush()
            {
                if (shortcut == null) return;
                while (body.Count > 0 && body[0].Trim().Length == 0) body.RemoveAt(0);
                while (body.Count > 0 && body[body.Count - 1].Trim().Length == 0) body.RemoveAt(body.Count - 1);
                if (!ValidShortcut.IsMatch(shortcut))
                    errors?.Add("Linha " + headerLine + ": atalho inválido \"" + shortcut + "\" (use letras, números e _; sem espaços).");
                else if (body.Count == 0)
                    errors?.Add("Linha " + headerLine + ": o snippet \"" + shortcut + "\" está vazio.");
                else
                {
                    var text = string.Join("\r\n", body.Select(l => l.TrimEnd()));
                    var sn = Make(shortcut, text, string.IsNullOrWhiteSpace(description) ? FirstLine(text) : description.Trim());
                    sn.IsUser = true;
                    sn.InStatement = true;
                    var dup = result.FindIndex(x => string.Equals(x.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase));
                    if (dup >= 0)
                    {
                        errors?.Add("Linha " + headerLine + ": o atalho \"" + shortcut + "\" já existe; vale este.");
                        result.RemoveAt(dup);
                    }
                    result.Add(sn);
                }
                shortcut = null;
                body.Clear();
            }

            for (int i = 0; i < lines.Length; i++)
            {
                var m = Header.Match(lines[i]);
                if (m.Success)
                {
                    Flush();
                    shortcut = m.Groups[1].Value;
                    description = m.Groups[2].Value;
                    headerLine = i + 1;
                }
                else if (shortcut != null) body.Add(lines[i]);
            }
            Flush();
            return result;
        }

        private static string FirstLine(string text)
        {
            var first = text.Split('\n')[0].Trim();
            return first.Length > 60 ? first.Substring(0, 57) + "..." : first;
        }

        /// <summary>Embutidos + os do usuário; um atalho do usuário igual a um embutido substitui o embutido.</summary>
        public static List<SqlSnippet> Merge(IEnumerable<SqlSnippet> user)
        {
            var list = (user ?? Enumerable.Empty<SqlSnippet>()).ToList();
            var names = new HashSet<string>(list.Select(x => x.Shortcut), StringComparer.OrdinalIgnoreCase);
            list.AddRange(BuiltIn.Where(b => !names.Contains(b.Shortcut)));
            return list;
        }

        /// <summary>
        /// Texto pronto para inserir: quebras de linha do documento e, a partir da segunda linha, a indentação da
        /// linha onde o atalho foi digitado. <paramref name="caret"/> é a posição do cursor no texto devolvido.
        /// </summary>
        public static string Expand(string text, int? cursorOffset, string indent, string newLine, out int caret)
        {
            var normalized = text.Replace("\r\n", "\n");
            var cursor = cursorOffset ?? text.Length;
            // posição no texto normalizado: desconta os \r antes do cursor
            var normalizedCursor = cursor - text.Substring(0, Math.Min(cursor, text.Length)).Count(ch => ch == '\r');
            var sb = new StringBuilder();
            caret = -1;
            for (int i = 0; i < normalized.Length; i++)
            {
                if (i == normalizedCursor) caret = sb.Length;
                if (normalized[i] == '\n') sb.Append(newLine).Append(indent);
                else sb.Append(normalized[i]);
            }
            if (caret < 0) caret = sb.Length;
            return sb.ToString();
        }

        /// <summary>Conteúdo inicial do arquivo de snippets do usuário.</summary>
        public const string Template =
            "-- QueryPilot: seus snippets. Salve o arquivo e eles já valem no autocomplete (digite o atalho + Tab).\r\n" +
            "--\r\n" +
            "-- Cada snippet começa com uma linha:   --#atalho  descrição (opcional)\r\n" +
            "-- O texto vai até o próximo --#. $CURSOR$ marca onde o cursor fica depois do Tab.\r\n" +
            "-- Um atalho igual a um embutido (ex.: ssf) substitui o embutido.\r\n" +
            "\r\n" +
            "--#sel5 Primeiras 5 linhas sem bloquear\r\n" +
            "SELECT TOP 5 *\r\n" +
            "FROM $CURSOR$ WITH (NOLOCK)\r\n" +
            "\r\n" +
            "--#quem Quem está conectado neste banco\r\n" +
            "SELECT session_id, login_name, host_name, program_name, status, last_request_start_time\r\n" +
            "FROM sys.dm_exec_sessions\r\n" +
            "WHERE database_id = DB_ID() AND is_user_process = 1\r\n" +
            "ORDER BY last_request_start_time DESC;\r\n";
    }
}
