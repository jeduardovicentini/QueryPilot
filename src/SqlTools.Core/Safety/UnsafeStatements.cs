using System;
using System.Collections.Generic;
using System.Linq;
using SqlTools.Completion;
using SqlTools.Sql;

namespace SqlTools.Safety
{
    public sealed class UnsafeStatement
    {
        /// <summary>DELETE, UPDATE ou TRUNCATE.</summary>
        public string Kind { get; set; }
        public string Target { get; set; }
        /// <summary>Linha (1-based) no texto analisado.</summary>
        public int Line { get; set; }

        public override string ToString() => Kind + " em " + Target + " (linha " + Line + ")";
    }

    /// <summary>
    /// Acha DELETE/UPDATE sem WHERE e TRUNCATE TABLE (ignora tabelas temporárias e variáveis de tabela,
    /// e UPDATE/DELETE com INNER JOIN, que já restringe as linhas).
    /// </summary>
    public static class UnsafeStatements
    {
        public static List<UnsafeStatement> Find(string sql)
        {
            var result = new List<UnsafeStatement>();
            if (string.IsNullOrWhiteSpace(sql)) return result;
            var t = SqlTokenizer.Tokenize(sql).Where(x => x.Kind != TokenKind.Comment).ToList();
            if (t.Count == 0) return result;
            var starts = SqlContextAnalyzer.SegmentStatements(t);

            int s = 0;
            while (s < t.Count)
            {
                int e = s;
                while (e + 1 < t.Count && starts[e + 1] == starts[s]) e++;
                var issue = Check(sql, t, s, e);
                if (issue != null) result.Add(issue);
                s = e + 1;
            }
            return result;
        }

        private static UnsafeStatement Check(string sql, List<SqlToken> t, int s, int e)
        {
            // instrução principal: a primeira palavra, ou a que vem depois das CTEs
            int k = s;
            if (t[s].Is("WITH"))
            {
                int depth = 0;
                k = -1;
                for (int i = s + 1; i <= e; i++)
                {
                    if (t[i].Kind == TokenKind.OpenParen) depth++;
                    else if (t[i].Kind == TokenKind.CloseParen) depth--;
                    else if (depth == 0 && t[i].Is("SELECT", "INSERT", "UPDATE", "DELETE", "MERGE")) { k = i; break; }
                }
                if (k < 0) return null;
            }

            if (t[k].Is("TRUNCATE"))
            {
                if (k + 1 > e || !t[k + 1].Is("TABLE")) return null;
                var name = ReadName(t, k + 2, e, out _);
                if (name == null || IsLocal(name)) return null;
                return new UnsafeStatement { Kind = "TRUNCATE", Target = name, Line = LineOf(sql, t[k].Start) };
            }

            if (!t[k].Is("DELETE", "UPDATE")) return null;
            if (k + 1 <= e && t[k + 1].Kind == TokenKind.OpenParen) return null; // UPDATE(col) em gatilho

            int d = 0;
            bool innerJoin = false;
            for (int i = k + 1; i <= e; i++)
            {
                if (t[i].Kind == TokenKind.OpenParen) d++;
                else if (t[i].Kind == TokenKind.CloseParen) d--;
                else if (d == 0 && t[i].Is("WHERE")) return null;
                else if (d == 0 && t[i].Is("JOIN") && !t[i - 1].Is("LEFT", "RIGHT", "FULL", "CROSS", "OUTER")) innerJoin = true;
            }
            if (innerJoin) return null;

            int j = k + 1;
            if (j <= e && t[j].Is("TOP"))
            {
                j++;
                if (j <= e && t[j].Kind == TokenKind.OpenParen)
                {
                    int depth = 0;
                    for (; j <= e; j++)
                    {
                        if (t[j].Kind == TokenKind.OpenParen) depth++;
                        else if (t[j].Kind == TokenKind.CloseParen && --depth == 0) { j++; break; }
                    }
                }
                else j++;
                if (j <= e && t[j].Is("PERCENT")) j++;
            }
            if (j <= e && t[j].Is("FROM")) j++;
            var target = ReadName(t, j, e, out _);
            if (target == null || IsLocal(target)) return null;
            if (IsAliasOfLocalTable(t, k, e, target)) return null;
            return new UnsafeStatement { Kind = t[k].Text.ToUpperInvariant(), Target = target, Line = LineOf(sql, t[k].Start) };
        }

        /// <summary>Nome com pontos (db.schema.tabela) a partir de <paramref name="i"/>.</summary>
        private static string ReadName(List<SqlToken> t, int i, int e, out int end)
        {
            end = i;
            if (i > e || !(t[i].IsName || t[i].Kind == TokenKind.Variable)) return null;
            var parts = new List<string> { t[i].Text };
            while (i + 2 <= e && t[i + 1].Kind == TokenKind.Dot && t[i + 2].IsName)
            {
                parts.Add(t[i + 2].Text);
                i += 2;
            }
            end = i;
            return string.Join(".", parts);
        }

        private static bool IsLocal(string name) => name.StartsWith("#") || name.StartsWith("@");

        /// <summary>DELETE x FROM #t x / UPDATE x SET ... FROM @t x.</summary>
        private static bool IsAliasOfLocalTable(List<SqlToken> t, int k, int e, string target)
        {
            for (int i = k + 1; i < e; i++)
            {
                if (!t[i].Is("FROM", "JOIN")) continue;
                var name = ReadName(t, i + 1, e, out int end);
                if (name == null || !IsLocal(name)) continue;
                int a = end + 1;
                if (a <= e && t[a].Is("AS")) a++;
                if (a <= e && t[a].IsName && string.Equals(t[a].Value, target.Trim('[', ']'), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int LineOf(string sql, int pos)
        {
            int line = 1;
            for (int i = 0; i < pos && i < sql.Length; i++) if (sql[i] == '\n') line++;
            return line;
        }
    }
}
