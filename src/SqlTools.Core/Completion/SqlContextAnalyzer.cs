using System;
using System.Collections.Generic;
using System.Linq;
using SqlTools.Sql;

namespace SqlTools.Completion
{
    public enum ContextKind
    {
        /// <summary>Sem sugestões (dentro de string/comentário, ou digitando um nome novo).</summary>
        None,
        /// <summary>Início de instrução: só palavras-chave.</summary>
        StatementStart,
        /// <summary>Lista do SELECT, WHERE, SET, etc.: colunas, funções, variáveis, palavras-chave.</summary>
        Expression,
        /// <summary>Depois de FROM/JOIN/UPDATE/INTO...: tabelas, views, funções de tabela, schemas.</summary>
        ObjectName,
        /// <summary>Depois de EXEC: procedures.</summary>
        Procedure,
        /// <summary>Depois de USE: bancos.</summary>
        Database,
        /// <summary>Depois de "algo.": colunas do alias/tabela ou objetos do schema.</summary>
        Member,
        /// <summary>Depois de ON (ou AND dentro do ON) de um JOIN.</summary>
        JoinCondition,
        /// <summary>Dentro de INSERT INTO t ( ... ).</summary>
        InsertColumns,
        /// <summary>Logo depois de INSERT INTO t.</summary>
        AfterInsertTarget,
        /// <summary>Logo depois de uma tabela no FROM (provavelmente vem um alias ou WHERE/JOIN).</summary>
        AfterTableRef,
        /// <summary>Digitando @variável.</summary>
        Variable,
        /// <summary>Cursor logo depois de * no SELECT.</summary>
        StarExpansion,
        /// <summary>Depois de EXEC proc (ou de uma vírgula na lista de parâmetros).</summary>
        ProcParameters
    }

    public sealed class TableRef
    {
        /// <summary>Partes do nome: [banco, schema, nome] (as que existirem).</summary>
        public List<string> Parts { get; } = new List<string>();
        public string Alias { get; set; }
        public int Position { get; set; }
        /// <summary>Subconsulta, CTE ou variável de tabela: as colunas vêm de <see cref="DerivedColumns"/>.</summary>
        public bool IsDerived { get; set; }
        public List<string> DerivedColumns { get; } = new List<string>();
        /// <summary>Tipos das colunas derivadas, quando conhecidos (tabela temporária/variável de tabela).</summary>
        public List<string> DerivedTypes { get; } = new List<string>();

        public string Name => Parts.Count == 0 ? null : Parts[Parts.Count - 1];
        public string Schema => Parts.Count >= 2 ? Parts[Parts.Count - 2] : null;
        public string Database => Parts.Count >= 3 ? Parts[Parts.Count - 3] : null;
        /// <summary>Como a tabela é referenciada nas colunas: o alias, ou o nome.</summary>
        public string RefName => string.IsNullOrEmpty(Alias) ? Name : Alias;

        public override string ToString() => string.Join(".", Parts) + (Alias == null ? "" : " " + Alias);
    }

    public sealed class SqlCompletionContext
    {
        public ContextKind Kind { get; set; }
        /// <summary>O que o usuário já digitou da palavra atual.</summary>
        public string Prefix { get; set; } = "";
        public int ReplaceStart { get; set; }
        public int ReplaceLength { get; set; }

        /// <summary>Para Member: as partes antes do ponto (ex.: ["dbo"] ou ["c"]).</summary>
        public List<string> Qualifier { get; } = new List<string>();
        /// <summary>Para Member: o qualificador está em posição de objeto (FROM dbo.|) ou de procedure (EXEC dbo.|).</summary>
        public ContextKind QualifierPosition { get; set; } = ContextKind.Expression;

        public bool IsJoin { get; set; }
        /// <summary>Para ObjectName: a palavra que abriu a posição (FROM, JOIN, UPDATE, INTO...).</summary>
        public string ObjectKeyword { get; set; }
        public List<TableRef> Tables { get; } = new List<TableRef>();
        public List<TableRef> Ctes { get; } = new List<TableRef>();
        public TableRef JoinTarget { get; set; }
        public TableRef InsertTarget { get; set; }
        public List<string> Variables { get; } = new List<string>();
        /// <summary>Tabelas temporárias (#t, ##t) do script e variáveis de tabela (@t) do batch, com colunas.</summary>
        public List<TableRef> LocalTables { get; } = new List<TableRef>();
        /// <summary>Para ProcParameters (e @ dentro de um EXEC): nome da procedure e parâmetros já informados.</summary>
        public List<string> ProcedureName { get; } = new List<string>();
        public List<string> UsedParameters { get; } = new List<string>();

        /// <summary>Para StarExpansion: o trecho a substituir (o * ou alias.*) e o qualificador, se houver.</summary>
        public int StarStart { get; set; }
        public int StarLength { get; set; }
        public string StarQualifier { get; set; }

        public override string ToString() => Kind + " prefix='" + Prefix + "' tables=[" + string.Join(", ", Tables) + "]";
    }

    /// <summary>Descobre, a partir do texto e da posição do cursor, o que faz sentido sugerir.</summary>
    public static class SqlContextAnalyzer
    {
        private static readonly string[] ObjectKeywords = { "FROM", "JOIN", "UPDATE", "INTO", "MERGE", "USING", "APPLY" };

        public static SqlCompletionContext Analyze(string text, int caret)
        {
            text = text ?? "";
            if (caret < 0) caret = 0;
            if (caret > text.Length) caret = text.Length;
            var ctx = new SqlCompletionContext { ReplaceStart = caret };
            var all = SqlTokenizer.Tokenize(text);

            // 1) palavra sob o cursor
            SqlToken word = null;
            foreach (var t in all)
            {
                if (t.Kind == TokenKind.Comment || t.Kind == TokenKind.String)
                {
                    var lineComment = t.Kind == TokenKind.Comment && t.Text.StartsWith("--");
                    if (caret > t.Start && (caret < t.End || (caret == t.End && (t.Unterminated || lineComment)))) { ctx.Kind = ContextKind.None; return ctx; }
                    continue;
                }
                if (caret > t.Start && caret <= t.End)
                {
                    if (t.Kind == TokenKind.Number) { ctx.Kind = ContextKind.None; return ctx; }
                    if (t.IsName || t.Kind == TokenKind.Variable) word = t;
                    break;
                }
            }
            if (word != null)
            {
                ctx.ReplaceStart = word.Start;
                ctx.ReplaceLength = word.Length;
                var typed = text.Substring(word.Start, caret - word.Start);
                ctx.Prefix = word.Kind == TokenKind.QuotedIdentifier ? typed.TrimStart('[', '"') : typed;
            }

            // 2) tokens significativos do batch atual (sem comentários, sem a palavra sob o cursor)
            var sig = all.Where(t => t.Kind != TokenKind.Comment).ToList();
            var batch = CurrentBatch(text, sig, caret);
            var wordIdx = word == null ? -1 : batch.IndexOf(word);

            ctx.Variables.AddRange(batch.Where(t => t.Kind == TokenKind.Variable && t != word && t.Text.Length > 1 && !t.Text.StartsWith("@@"))
                .Select(t => t.Text).Distinct(StringComparer.OrdinalIgnoreCase));

            CollectLocalTables(sig, batch, word, ctx.LocalTables);

            // 3) instrução atual
            var before = batch.Where(t => t.End <= ctx.ReplaceStart && t != word).ToList();
            if (word != null && word.Kind == TokenKind.Variable)
            {
                DetectExec(before, ctx);
                ctx.Kind = ContextKind.Variable;
                return ctx;
            }
            var stmtStarts = SegmentStatements(batch);
            int lastBeforeIdx = before.Count == 0 ? -1 : batch.IndexOf(before[before.Count - 1]);
            if (lastBeforeIdx >= 0 && batch[lastBeforeIdx].Kind == TokenKind.Semicolon)
            {
                ctx.Kind = ContextKind.StatementStart;
                return ctx;
            }
            int stmtStart = lastBeforeIdx < 0 ? 0 : stmtStarts[lastBeforeIdx];
            int stmtEnd = batch.Count;
            for (int i = Math.Max(lastBeforeIdx + 1, 0); i < batch.Count; i++)
            {
                if (i == wordIdx) continue;
                if (stmtStarts[i] == i && i > lastBeforeIdx) { stmtEnd = i; break; }
                if (batch[i].Kind == TokenKind.Semicolon) { stmtEnd = i; break; }
            }
            var stmt = batch.GetRange(stmtStart, stmtEnd - stmtStart);
            var stmtBefore = stmt.Where(t => t.End <= ctx.ReplaceStart && t != word).ToList();

            CollectCtes(stmt, ctx.Ctes);
            CollectTableRefs(stmt, word, ctx.Tables);

            if (stmtBefore.Count == 0)
            {
                ctx.Kind = ContextKind.StatementStart;
                return ctx;
            }

            ctx.Kind = Classify(stmt, stmtBefore, ctx);
            return ctx;
        }

        private static ContextKind Classify(List<SqlToken> stmt, List<SqlToken> before, SqlCompletionContext ctx)
        {
            var last = before[before.Count - 1];
            var prev = before.Count >= 2 ? before[before.Count - 2] : null;

            // alias.coluna / schema.objeto
            if (last.Kind == TokenKind.Dot)
            {
                int i = before.Count - 1;
                var parts = new List<string>();
                while (i >= 0 && before[i].Kind == TokenKind.Dot)
                {
                    if (i - 1 >= 0 && before[i - 1].IsName) { parts.Insert(0, before[i - 1].Value); i -= 2; }
                    else { parts.Insert(0, ""); i -= 1; }
                }
                if (parts.Count == 0 || parts.All(string.IsNullOrEmpty)) return ContextKind.None;
                ctx.Qualifier.AddRange(parts);
                var head = i >= 0 ? before[i] : null;
                if (head != null && head.Is("EXEC", "EXECUTE")) ctx.QualifierPosition = ContextKind.Procedure;
                else if (head != null && (head.Is(ObjectKeywords) || head.Is("TABLE", "VIEW", "DELETE") || (head.Kind == TokenKind.Comma && ClauseOf(before, i) == "FROM")))
                {
                    ctx.QualifierPosition = ContextKind.ObjectName;
                    ctx.IsJoin = head.Is("JOIN");
                    ctx.ObjectKeyword = head.Kind == TokenKind.Comma ? "FROM" : head.Text.ToUpperInvariant();
                }
                return ContextKind.Member;
            }

            // SELECT * / SELECT a.*
            if (last.Kind == TokenKind.Star && ctx.Prefix.Length == 0)
            {
                int starStart = last.Start;
                string qualifier = null;
                var p = prev;
                if (p != null && p.Kind == TokenKind.Dot && before.Count >= 3 && before[before.Count - 3].IsName)
                {
                    qualifier = before[before.Count - 3].Value;
                    starStart = before[before.Count - 3].Start;
                    p = before.Count >= 4 ? before[before.Count - 4] : null;
                }
                if (p != null && (p.Is("SELECT", "DISTINCT", "ALL") || p.Kind == TokenKind.Comma || p.Kind == TokenKind.Number || p.Kind == TokenKind.CloseParen))
                {
                    ctx.StarStart = starStart;
                    ctx.StarLength = last.End - starStart;
                    ctx.StarQualifier = qualifier;
                    return ContextKind.StarExpansion;
                }
                return ContextKind.None;
            }

            if (last.Is("AS", "DECLARE")) return ContextKind.None;
            if (last.Is("TABLE", "VIEW", "PROC", "PROCEDURE", "FUNCTION", "TRIGGER", "INDEX", "SCHEMA", "DATABASE", "TYPE", "SYNONYM", "LOGIN", "USER", "ROLE"))
            {
                if (prev != null && prev.Is("CREATE")) return ContextKind.None;
                if (last.Is("PROC", "PROCEDURE")) return ContextKind.Procedure;
                if (last.Is("TABLE", "VIEW", "FUNCTION")) return ContextKind.ObjectName;
                if (last.Is("DATABASE")) return ContextKind.Database;
                return ContextKind.None;
            }
            if (last.Is("WITH") && FirstIs(stmt, "WITH") && before.Count == 1) return ContextKind.None;

            if (last.Is("EXEC", "EXECUTE")) return ContextKind.Procedure;
            if (last.Is("USE")) return ContextKind.Database;

            // EXEC proc ␣ / EXEC proc @a = 1, ␣
            var nameEnd = DetectExec(before, ctx);
            if (nameEnd >= 0 && (nameEnd == before.Count || last.Kind == TokenKind.Comma)) return ContextKind.ProcParameters;

            if (last.Is(ObjectKeywords) || last.Is("DELETE", "INSERT"))
            {
                ctx.IsJoin = last.Is("JOIN");
                ctx.ObjectKeyword = last.Text.ToUpperInvariant();
                if (last.Is("UPDATE") && prev != null && prev.Is("THEN")) return ContextKind.Expression;
                return ContextKind.ObjectName;
            }

            // INSERT INTO t | e INSERT INTO t ( |
            var insert = FindInsertTarget(before, ctx);
            if (insert != ContextKind.None) return insert;

            var clause = ClauseOf(before, before.Count);
            if (last.Kind == TokenKind.Comma && clause == "FROM") { ctx.ObjectKeyword = "FROM"; return ContextKind.ObjectName; }

            if (last.Is("ON") || (clause == "ON" && last.Is("AND", "OR")))
            {
                ctx.JoinTarget = ctx.Tables.LastOrDefault(t => t.Position < last.Start);
                return ContextKind.JoinCondition;
            }

            // logo depois de uma tabela do FROM/JOIN (alias ou próxima cláusula)
            if (last.IsName && ctx.Tables.Any(t => EndsAt(stmt, t, last))) return ContextKind.AfterTableRef;

            return ContextKind.Expression;
        }

        /// <summary>O token é o último de uma referência de tabela (nome ou alias)?</summary>
        private static bool EndsAt(List<SqlToken> stmt, TableRef t, SqlToken last)
        {
            if (t.IsDerived && t.Alias == null) return false;
            var idx = stmt.IndexOf(last);
            if (idx < 0) return false;
            if (t.Alias != null) return string.Equals(last.Value, t.Alias, StringComparison.OrdinalIgnoreCase) && last.Start > t.Position;
            return last.Start >= t.Position && string.Equals(last.Value, t.Name, StringComparison.OrdinalIgnoreCase)
                && (idx + 1 >= stmt.Count || stmt[idx + 1].Kind != TokenKind.Dot);
        }

        private static ContextKind FindInsertTarget(List<SqlToken> before, SqlCompletionContext ctx)
        {
            // procura INSERT [INTO] nome [( ...] no fim do trecho antes do cursor
            for (int i = before.Count - 1; i >= 0; i--)
            {
                if (!before[i].Is("INSERT")) continue;
                int j = i + 1;
                if (j < before.Count && before[j].Is("INTO")) j++;
                var parts = new List<string>();
                int k = ReadName(before, j, parts);
                if (parts.Count == 0) return ContextKind.None;
                var target = new TableRef { Position = before[j].Start };
                target.Parts.AddRange(parts);
                if (k == before.Count)
                {
                    ctx.InsertTarget = target;
                    return ContextKind.AfterInsertTarget;
                }
                if (before[k].Kind == TokenKind.OpenParen)
                {
                    for (int m = k + 1; m < before.Count; m++)
                    {
                        if (before[m].Kind == TokenKind.CloseParen) return ContextKind.None;
                        if (before[m].Kind != TokenKind.Comma && !before[m].IsName) return ContextKind.None;
                    }
                    var lastTok = before[before.Count - 1];
                    if (lastTok.Kind == TokenKind.OpenParen || lastTok.Kind == TokenKind.Comma)
                    {
                        ctx.InsertTarget = target;
                        return ContextKind.InsertColumns;
                    }
                }
                return ContextKind.None;
            }
            return ContextKind.None;
        }

        /// <summary>A cláusula (FROM, WHERE, SELECT, ON...) em que está a posição, no mesmo nível de parênteses.</summary>
        private static string ClauseOf(List<SqlToken> tokens, int endExclusive)
        {
            int depth = 0;
            for (int i = endExclusive - 1; i >= 0; i--)
            {
                var t = tokens[i];
                if (t.Kind == TokenKind.CloseParen) { depth++; continue; }
                if (t.Kind == TokenKind.OpenParen) { if (depth == 0) return "("; depth--; continue; }
                if (depth > 0) continue;
                if (t.Is("FROM", "WHERE", "SELECT", "HAVING", "ON", "SET", "VALUES", "BY", "INTO", "USING")) return t.Text.ToUpperInvariant();
                if (t.Is("JOIN", "APPLY")) return "FROM";
            }
            return null;
        }

        private static bool FirstIs(List<SqlToken> stmt, string word) => stmt.Count > 0 && stmt[0].Is(word);

        /// <summary>Lê nome[.nome[.nome]] a partir de i; devolve o índice depois do nome.</summary>
        private static int ReadName(List<SqlToken> t, int i, List<string> parts)
        {
            if (i >= t.Count) return i;
            if (t[i].Kind == TokenKind.Variable) { parts.Add(t[i].Text); return i + 1; }
            if (!t[i].IsName || (t[i].Kind == TokenKind.Identifier && SqlKeywords.Reserved.Contains(t[i].Text))) return i;
            parts.Add(t[i].Value);
            i++;
            while (i < t.Count && t[i].Kind == TokenKind.Dot)
            {
                if (i + 1 < t.Count && t[i + 1].IsName) { parts.Add(t[i + 1].Value); i += 2; }
                else if (i + 1 < t.Count && t[i + 1].Kind == TokenKind.Dot) { parts.Add(""); i += 1; }
                else { i++; break; }
            }
            return i;
        }

        private static void CollectTableRefs(List<SqlToken> t, SqlToken word, List<TableRef> refs)
        {
            for (int i = 0; i < t.Count; i++)
            {
                var k = t[i];
                bool starts = k.Is(ObjectKeywords)
                    || (k.Is("TABLE") && i > 0 && t[i - 1].Is("TRUNCATE"))
                    || (k.Is("DELETE") && i + 1 < t.Count && !t[i + 1].Is("FROM"));
                if (!starts) continue;
                if (k.Is("UPDATE") && i > 0 && t[i - 1].Is("THEN")) continue;

                int j = i + 1;
                if (k.Is("MERGE") && j < t.Count && t[j].Is("INTO")) j++;
                while (j < t.Count)
                {
                    var r = ParseRef(t, ref j, word);
                    if (r == null) break;
                    refs.Add(r);
                    if (k.Is("FROM") && j < t.Count && t[j].Kind == TokenKind.Comma) { j++; continue; }
                    break;
                }
            }

            // UPDATE c ... FROM Cliente c: o "c" do UPDATE é só o alias de outra tabela
            refs.RemoveAll(r => r.Parts.Count == 1 && r.Alias == null && !r.IsDerived
                && refs.Any(o => o != r && string.Equals(o.Alias, r.Name, StringComparison.OrdinalIgnoreCase)));
        }

        private static TableRef ParseRef(List<SqlToken> t, ref int j, SqlToken word)
        {
            if (j >= t.Count) return null;
            var r = new TableRef { Position = t[j].Start };

            if (t[j].Kind == TokenKind.OpenParen)
            {
                int close = MatchParen(t, j);
                r.IsDerived = true;
                r.DerivedColumns.AddRange(SelectListNames(t, j + 1, close < 0 ? t.Count : close));
                j = close < 0 ? t.Count : close + 1;
            }
            else
            {
                if (t[j] == word) return null;
                var parts = new List<string>();
                int next = ReadName(t, j, parts);
                if (parts.Count == 0) return null;
                r.Parts.AddRange(parts);
                if (parts.Count == 1 && parts[0].StartsWith("@")) r.IsDerived = true;
                j = next;
                if (j < t.Count && t[j].Kind == TokenKind.OpenParen)
                {
                    int close = MatchParen(t, j);
                    j = close < 0 ? t.Count : close + 1;
                }
            }

            if (j < t.Count && t[j].Is("AS")) j++;
            if (j < t.Count && t[j].IsName && t[j] != word && !(t[j].Kind == TokenKind.Identifier && SqlKeywords.Reserved.Contains(t[j].Text)))
            {
                r.Alias = t[j].Value;
                j++;
            }
            // dica de tabela: WITH (NOLOCK)
            if (j + 1 < t.Count && t[j].Is("WITH") && t[j + 1].Kind == TokenKind.OpenParen)
            {
                int close = MatchParen(t, j + 1);
                j = close < 0 ? t.Count : close + 1;
            }
            return r;
        }

        private static int MatchParen(List<SqlToken> t, int open)
        {
            int depth = 0;
            for (int i = open; i < t.Count; i++)
            {
                if (t[i].Kind == TokenKind.OpenParen) depth++;
                else if (t[i].Kind == TokenKind.CloseParen && --depth == 0) return i;
            }
            return -1;
        }

        /// <summary>Nomes das colunas do SELECT em t[from..to) (alias, "nome =" ou última parte do nome).</summary>
        public static List<string> SelectListNames(List<SqlToken> t, int from, int to)
        {
            var names = new List<string>();
            int sel = -1;
            for (int i = from; i < to; i++) if (t[i].Is("SELECT")) { sel = i; break; }
            if (sel < 0) return names;

            int depth = 0, itemStart = sel + 1;
            if (itemStart < to && t[itemStart].Is("DISTINCT", "ALL")) itemStart++;
            if (itemStart < to && t[itemStart].Is("TOP"))
            {
                itemStart++;
                if (itemStart < to && t[itemStart].Kind == TokenKind.OpenParen) { int c = MatchParen(t, itemStart); itemStart = c < 0 ? to : c + 1; }
                else itemStart++;
                if (itemStart < to && t[itemStart].Is("PERCENT")) itemStart++;
            }
            for (int i = itemStart; i <= to; i++)
            {
                bool end = i == to;
                if (!end)
                {
                    if (t[i].Kind == TokenKind.OpenParen) depth++;
                    else if (t[i].Kind == TokenKind.CloseParen) depth--;
                    end = depth == 0 && (t[i].Kind == TokenKind.Comma || t[i].Is("FROM", "INTO", "WHERE", "GROUP", "ORDER", "UNION"));
                }
                if (!end) continue;
                var n = ItemName(t, itemStart, i);
                if (!string.IsNullOrEmpty(n)) names.Add(n);
                if (i < to && t[i].Kind != TokenKind.Comma) break;
                itemStart = i + 1;
            }
            return names;
        }

        private static string ItemName(List<SqlToken> t, int from, int to)
        {
            if (to <= from) return null;
            for (int i = from; i < to - 1; i++)
                if (t[i].Is("AS") && t[i + 1].IsName) return t[i + 1].Value;
            if (to - from >= 2 && t[from].IsName && t[from + 1].Kind == TokenKind.Operator && t[from + 1].Text == "=") return t[from].Value;
            var last = t[to - 1];
            return last.IsName ? last.Value : null;
        }

        private static void CollectCtes(List<SqlToken> t, List<TableRef> ctes)
        {
            if (t.Count == 0 || !t[0].Is("WITH")) return;
            int i = 1;
            while (i < t.Count && t[i].IsName)
            {
                var cte = new TableRef { Position = t[i].Start, IsDerived = true };
                cte.Parts.Add(t[i].Value);
                i++;
                if (i < t.Count && t[i].Kind == TokenKind.OpenParen)
                {
                    int close = MatchParen(t, i);
                    for (int k = i + 1; k < (close < 0 ? t.Count : close); k++) if (t[k].IsName) cte.DerivedColumns.Add(t[k].Value);
                    i = close < 0 ? t.Count : close + 1;
                }
                if (i < t.Count && t[i].Is("AS")) i++;
                if (i < t.Count && t[i].Kind == TokenKind.OpenParen)
                {
                    int close = MatchParen(t, i);
                    if (cte.DerivedColumns.Count == 0) cte.DerivedColumns.AddRange(SelectListNames(t, i + 1, close < 0 ? t.Count : close));
                    i = close < 0 ? t.Count : close + 1;
                }
                ctes.Add(cte);
                if (i < t.Count && t[i].Kind == TokenKind.Comma) { i++; continue; }
                break;
            }
        }

        /// <summary>
        /// Se o fim de <paramref name="before"/> está dentro de um EXEC (nome da procedure + lista de parâmetros),
        /// preenche ProcedureName/UsedParameters e devolve o índice logo depois do nome; senão, -1.
        /// </summary>
        private static int DetectExec(List<SqlToken> before, SqlCompletionContext ctx)
        {
            int e = -1;
            for (int i = before.Count - 1; i >= 0; i--)
            {
                var t = before[i];
                if (t.Is("EXEC", "EXECUTE")) { e = i; break; }
                if (t.Kind == TokenKind.Semicolon || t.Kind == TokenKind.OpenParen || t.Kind == TokenKind.CloseParen) return -1;
                if (t.Kind == TokenKind.Identifier && SqlKeywords.StatementStarters.Contains(t.Text)) return -1;
            }
            if (e < 0) return -1;
            int j = e + 1;
            // EXEC @ret = proc
            if (j + 1 < before.Count && before[j].Kind == TokenKind.Variable && before[j + 1].Text == "=") j += 2;
            var parts = new List<string>();
            int end = ReadName(before, j, parts);
            if (parts.Count == 0 || parts[0].StartsWith("@")) return -1;
            ctx.ProcedureName.Clear();
            ctx.ProcedureName.AddRange(parts);
            for (int k = end; k + 1 < before.Count; k++)
                if (before[k].Kind == TokenKind.Variable && before[k + 1].Text == "=") ctx.UsedParameters.Add(before[k].Text);
            return end;
        }

        /// <summary>CREATE TABLE #t (...), SELECT ... INTO #t e DECLARE @t TABLE (...).</summary>
        private static void CollectLocalTables(List<SqlToken> all, List<SqlToken> batch, SqlToken word, List<TableRef> result)
        {
            var batchSet = new HashSet<SqlToken>(batch);
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == word) continue;
                TableRef r = null;
                if (t.Kind == TokenKind.Identifier && t.Text.StartsWith("#") && i >= 2 && all[i - 1].Is("TABLE") && all[i - 2].Is("CREATE")
                    && i + 1 < all.Count && all[i + 1].Kind == TokenKind.OpenParen)
                {
                    r = NewLocal(t);
                    ColumnDefs(all, i + 1, r);
                }
                else if (t.Kind == TokenKind.Variable && batchSet.Contains(t) && i + 2 < all.Count && all[i + 1].Is("TABLE") && all[i + 2].Kind == TokenKind.OpenParen
                    && i >= 1 && (all[i - 1].Is("DECLARE") || all[i - 1].Kind == TokenKind.Comma))
                {
                    r = NewLocal(t);
                    ColumnDefs(all, i + 2, r);
                }
                else if (t.Kind == TokenKind.Identifier && t.Text.StartsWith("#") && i >= 1 && all[i - 1].Is("INTO"))
                {
                    // SELECT ... INTO #t: o SELECT do mesmo nível, antes do INTO
                    int depth = 0, sel = -1;
                    for (int k = i - 2; k >= 0; k--)
                    {
                        if (all[k].Kind == TokenKind.CloseParen) depth++;
                        else if (all[k].Kind == TokenKind.OpenParen) { if (depth == 0) break; depth--; }
                        else if (depth == 0 && all[k].Is("SELECT")) { sel = k; break; }
                        else if (depth == 0 && all[k].Kind == TokenKind.Semicolon) break;
                    }
                    if (sel < 0) continue;
                    r = NewLocal(t);
                    r.DerivedColumns.AddRange(SelectListNames(all, sel, i - 1));
                    foreach (var _ in r.DerivedColumns) r.DerivedTypes.Add(null);
                }
                if (r != null && !result.Any(x => string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase))) result.Add(r);
            }
        }

        private static TableRef NewLocal(SqlToken t)
        {
            var r = new TableRef { Position = t.Start, IsDerived = true };
            r.Parts.Add(t.Kind == TokenKind.Variable ? t.Text : t.Value);
            return r;
        }

        private static readonly string[] NotColumns = { "CONSTRAINT", "PRIMARY", "UNIQUE", "INDEX", "CHECK", "FOREIGN", "PERIOD" };

        /// <summary>Lê "(col tipo ..., col tipo ...)" a partir do parêntese em <paramref name="open"/>.</summary>
        private static void ColumnDefs(List<SqlToken> t, int open, TableRef r)
        {
            int close = MatchParen(t, open);
            if (close < 0) close = t.Count;
            int i = open + 1;
            while (i < close)
            {
                if (t[i].IsName && !t[i].Is(NotColumns))
                {
                    var name = t[i].Value;
                    string type = null;
                    if (i + 1 < close && t[i + 1].IsName)
                    {
                        type = t[i + 1].Value.ToLowerInvariant();
                        if (i + 2 < close && t[i + 2].Kind == TokenKind.OpenParen)
                        {
                            int c = MatchParen(t, i + 2);
                            if (c > 0) type += "(" + string.Concat(t.Skip(i + 3).Take(c - i - 3).Select(x => x.Text)) + ")";
                        }
                    }
                    r.DerivedColumns.Add(name);
                    r.DerivedTypes.Add(type);
                }
                // próximo item: depois da vírgula no nível do parêntese
                int depth = 0;
                for (; i < close; i++)
                {
                    if (t[i].Kind == TokenKind.OpenParen) depth++;
                    else if (t[i].Kind == TokenKind.CloseParen) depth--;
                    else if (depth == 0 && t[i].Kind == TokenKind.Comma) { i++; break; }
                }
            }
        }

        /// <summary>Tokens do batch (separado por GO) que contém o cursor.</summary>
        private static List<SqlToken> CurrentBatch(string text, List<SqlToken> sig, int caret)
        {
            int start = 0;
            var result = new List<SqlToken>();
            for (int i = 0; i < sig.Count; i++)
            {
                if (IsGo(text, sig[i]))
                {
                    if (sig[i].Start >= caret) return sig.GetRange(start, i - start);
                    start = i + 1;
                }
            }
            return sig.GetRange(start, sig.Count - start);
        }

        private static bool IsGo(string text, SqlToken t)
        {
            if (!t.Is("GO")) return false;
            for (int i = t.Start - 1; i >= 0 && text[i] != '\n'; i--) if (!char.IsWhiteSpace(text[i])) return false;
            for (int i = t.End; i < text.Length && text[i] != '\n'; i++) if (!char.IsWhiteSpace(text[i]) && !char.IsDigit(text[i])) return false;
            return true;
        }

        /// <summary>Para cada token, o índice do token que começa a instrução dele.</summary>
        public static int[] SegmentStatements(List<SqlToken> t)
        {
            var starts = new int[t.Count];
            int cur = 0, depth = 0, caseDepth = 0;
            string first = null, main = null;
            bool seenSelectOrValues = false, seenSet = false;

            for (int i = 0; i < t.Count; i++)
            {
                var k = t[i];
                var prev = i > 0 ? t[i - 1] : null;

                if (prev != null && prev.Kind == TokenKind.Semicolon && depth == 0) { cur = i; first = main = null; seenSelectOrValues = seenSet = false; caseDepth = 0; }

                if (depth == 0 && i != cur && k.Kind == TokenKind.Identifier && SqlKeywords.StatementStarters.Contains(k.Text) && !Continues(t, i, first, main, seenSelectOrValues, seenSet, caseDepth))
                {
                    cur = i; first = main = null; seenSelectOrValues = seenSet = false; caseDepth = 0;
                }

                if (i == cur && k.Kind == TokenKind.Identifier) { first = k.Text.ToUpperInvariant(); main = first == "WITH" ? null : first; }
                if (depth == 0 && first == "WITH" && main == null && i != cur && k.Is("SELECT", "INSERT", "UPDATE", "DELETE", "MERGE")) main = k.Text.ToUpperInvariant();
                if (k.Is("SELECT", "VALUES")) seenSelectOrValues = true;
                if (k.Is("SET") && depth == 0) seenSet = true;
                if (k.Is("CASE")) caseDepth++;
                else if (k.Is("END") && caseDepth > 0) caseDepth--;

                if (k.Kind == TokenKind.OpenParen) depth++;
                else if (k.Kind == TokenKind.CloseParen && depth > 0) depth--;

                starts[i] = cur;
            }
            return starts;
        }

        private static bool Continues(List<SqlToken> t, int i, string first, string main, bool seenSelectOrValues, bool seenSet, int caseDepth)
        {
            var k = t[i];
            var prev = t[i - 1];
            var next = i + 1 < t.Count ? t[i + 1] : null;

            if (caseDepth > 0 && k.Is("END", "ELSE")) return true;
            // gatilhos (AFTER INSERT, UPDATE), cursores (FOR SELECT / FOR UPDATE), ON DELETE CASCADE, MERGE ... THEN UPDATE
            if (prev.Kind == TokenKind.Comma && k.Is("INSERT", "UPDATE", "DELETE")) return true;
            if (prev.Is("FOR", "AFTER", "OF") && k.Is("INSERT", "UPDATE", "DELETE", "SELECT")) return true;
            if (prev.Is("ON") && k.Is("DELETE", "UPDATE")) return true;
            if (prev.Is("THEN") && k.Is("INSERT", "UPDATE", "DELETE")) return true;

            if (k.Is("SELECT"))
            {
                if (prev.Is("UNION", "ALL", "EXCEPT", "INTERSECT")) return true;
                if (main == "INSERT" && !seenSelectOrValues) return true;
                if (first == "WITH" && main == null) return true;
                return false;
            }
            if (k.Is("INSERT", "UPDATE", "DELETE", "MERGE")) return first == "WITH" && main == null;
            if (k.Is("SET")) return (main == "UPDATE" && !seenSet) || main == "MERGE";
            if (k.Is("WITH"))
            {
                // WITH (NOLOCK), WITH TIES, WITH CHECK OPTION... continuam; WITH cte AS ( começa
                return !(next != null && next.IsName && i + 2 < t.Count && (t[i + 2].Is("AS") || t[i + 2].Kind == TokenKind.OpenParen));
            }
            if (k.Is("EXEC", "EXECUTE")) return main == "INSERT" && !seenSelectOrValues;
            return false;
        }
    }
}
