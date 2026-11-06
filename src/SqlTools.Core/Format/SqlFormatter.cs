using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlTools.Sql;

namespace SqlTools.Format
{
    public enum KeywordCase { Upper, Lower, Keep }

    public sealed class FormatOptions
    {
        public KeywordCase KeywordCase { get; set; } = KeywordCase.Upper;
        public int IndentSize { get; set; } = 4;
        public bool UseTabs { get; set; }
        /// <summary>Vírgula no começo da linha (", coluna") em vez do fim.</summary>
        public bool LeadingCommas { get; set; }
        /// <summary>Subconsultas, CASE e listas (exceto a do SELECT) até este tamanho ficam numa linha (0 = sempre quebrar).</summary>
        public int InlineMaxLength { get; set; } = 60;
    }

    public sealed class FormatResult
    {
        public bool Success { get; }
        public string Text { get; }
        public string Error { get; }

        private FormatResult(bool ok, string text, string error) { Success = ok; Text = text; Error = error; }
        internal static FormatResult Ok(string text) => new FormatResult(true, text, null);
        internal static FormatResult Fail(string error) => new FormatResult(false, null, error);
    }

    /// <summary>
    /// Formatador de T-SQL baseado em tokens: só muda espaços, quebras de linha e a caixa das palavras-chave.
    /// Comentários são preservados. Depois de formatar, o resultado é tokenizado de novo e comparado com o
    /// original; qualquer diferença cancela a formatação.
    /// </summary>
    public static class SqlFormatter
    {
        public static FormatResult Format(string sql, FormatOptions options = null)
        {
            options = options ?? new FormatOptions();
            if (string.IsNullOrWhiteSpace(sql)) return FormatResult.Ok(sql ?? "");
            var tokens = SqlTokenizer.Tokenize(sql);
            var open = tokens.FirstOrDefault(t => t.Unterminated);
            if (open != null)
                return FormatResult.Fail(open.Kind == TokenKind.Comment ? "comentário /* sem fechamento" :
                    open.Kind == TokenKind.String ? "string sem a aspa de fechamento" : "identificador sem o fechamento ] ou \"");

            string text;
            try { text = new Writer(sql, tokens, options).Run(); }
            catch (Exception ex) { return FormatResult.Fail("erro interno do formatador: " + ex.Message); }

            var problem = Verify(tokens, SqlTokenizer.Tokenize(text));
            return problem == null ? FormatResult.Ok(text) : FormatResult.Fail("verificação falhou (" + problem + ")");
        }

        /// <summary>Formata e aplica um recuo base a todas as linhas (para formatar só uma seleção).</summary>
        public static FormatResult Format(string sql, FormatOptions options, string baseIndent)
        {
            var r = Format(sql, options);
            if (!r.Success || string.IsNullOrEmpty(baseIndent)) return r;
            var nl = r.Text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = r.Text.Split(new[] { nl }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Length > 0) lines[i] = baseIndent + lines[i];
            return FormatResult.Ok(string.Join(nl, lines));
        }

        private static string Verify(List<SqlToken> a, List<SqlToken> b)
        {
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
            {
                var x = a[i]; var y = b[i];
                bool same = x.Kind == y.Kind && (
                    x.Kind == TokenKind.Identifier ? string.Equals(x.Text, y.Text, StringComparison.OrdinalIgnoreCase) :
                    x.Kind == TokenKind.Comment ? x.Text.TrimEnd() == y.Text.TrimEnd() :
                    x.Text == y.Text);
                if (!same) return "token " + (i + 1) + ": '" + x.Text + "' virou '" + y.Text + "'";
            }
            return a.Count == b.Count ? null : "quantidade de tokens mudou de " + a.Count + " para " + b.Count;
        }

        private static readonly HashSet<string> FunctionWords = new HashSet<string>(
            SqlKeywords.Functions.Select(f => f.Key).Where(k => k != "EXISTS").Concat(new[]
            {
                "CAST", "CONVERT", "TRY_CONVERT", "TRY_CAST", "COALESCE", "NULLIF", "ISNULL", "LEFT", "RIGHT", "IDENTITY",
                "CHECKSUM", "CONTAINS", "FREETEXT", "CONTAINSTABLE", "FREETEXTTABLE", "OPENQUERY", "OPENROWSET",
                "OPENDATASOURCE", "OPENXML", "OPENJSON", "USER_NAME", "UPDATE", "CHAR", "NCHAR", "RAISERROR"
            }), StringComparer.OrdinalIgnoreCase);

        /// <summary>Não reservadas, mas praticamente nunca usadas como nome de coluna/tabela.</summary>
        private static readonly HashSet<string> SafeKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NOCOUNT", "XACT_ABORT", "ANSI_NULLS", "ANSI_WARNINGS", "ANSI_PADDING", "QUOTED_IDENTIFIER", "ARITHABORT",
            "CONCAT_NULL_YIELDS_NULL", "NUMERIC_ROUNDABORT", "NOLOCK", "READUNCOMMITTED", "READCOMMITTED", "UPDLOCK",
            "ROWLOCK", "PAGLOCK", "TABLOCK", "TABLOCKX", "XLOCK", "READPAST", "NOWAIT", "SERIALIZABLE", "RECOMPILE",
            "MAXDOP", "TIES", "UNBOUNDED", "PRECEDING", "FOLLOWING", "RETURNS", "READONLY", "SCHEMABINDING", "INSTEAD",
            "FAST_FORWARD", "FORWARD_ONLY", "READ_ONLY", "TRY", "CATCH", "OUT", "ROWS", "ONLY", "ISOLATION",
            "UNCOMMITTED", "COMMITTED", "REPEATABLE", "INCLUDE", "PERSISTED"
        };

        /// <summary>Palavras que o Reserved inclui por conveniência do autocomplete, mas podem ser nomes de coluna.</summary>
        private static readonly HashSet<string> NotReallyReserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "APPLY", "OUTPUT", "OFFSET", "GO", "THROW", "WINDOW", "MATCHED"
        };

        private static readonly HashSet<string> FetchWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NEXT", "FIRST", "PRIOR", "LAST", "ABSOLUTE", "RELATIVE"
        };

        private static readonly HashSet<string> ListTerminators = new HashSet<string>(
            SqlKeywords.StatementStarters.Concat(new[]
            {
                "FROM", "WHERE", "INTO", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "OPTION", "WINDOW",
                "VALUES", "OUTPUT", "RETURNS", "GO", "FOR", "OFFSET", "WHEN", "CHECKPOINT", "KILL"
            }), StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> ObjectKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TABLE", "VIEW", "PROC", "PROCEDURE", "FUNCTION", "INDEX", "TRIGGER", "SCHEMA", "DATABASE", "TYPE", "SEQUENCE",
            "SYNONYM", "USER", "ROLE", "DEFAULT", "STATISTICS", "COLUMN", "CONSTRAINT", "LOGIN", "ASSEMBLY", "RULE"
        };

        private sealed class Writer
        {
            private enum ScopeKind { Block, Subquery, Inline, List, Case }
            private enum Stmt { None, Select, Insert, Update, Delete, Merge, With, Declare, Set, Exec, If, While, Else, Create, Alter, Drop, Fetch, Begin, Other }
            private enum Clause { None, Select, Into, From, Join, On, Where, GroupBy, Having, OrderBy, Offset, Union, Set, Values, Output, InsertTarget, With, Header, ProcParams, Body, Condition, Other }
            private enum Start { None, New, Continuation }

            private sealed class Scope
            {
                public ScopeKind Kind;
                public int Base;          // recuo das instruções / itens deste escopo
                public int OpenIndent;    // recuo da linha que abriu o escopo
                public bool Breaks = true;
                public Stmt Stmt;
                public Clause Clause;
                public int StmtIndent;
                public bool AtStart = true;
                public bool ListBreak;
                public int ListIndent;
                public int ListFirst = -1;
                public bool ListIsSelect;
                public bool ListAsTerminator;
                public bool AwaitBody;
                public int SingleDepth;
                public readonly List<int> IfIndents = new List<int>();
                public bool Between;
                public bool JoinPhrase;
                public bool ExpectTableDef;
                public bool Routine;
                public bool IsStatementScope => Kind == ScopeKind.Block || Kind == ScopeKind.Subquery;
            }

            private readonly string _src;
            private readonly List<SqlToken> _t;
            private readonly FormatOptions _o;
            private readonly string _nl;
            private readonly int[] _nlBefore;
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly List<Scope> _stack = new List<Scope>();
            private readonly List<int> _pendingComments = new List<int>();
            private int _lineIndent;
            private int? _pendingBreak;
            private SqlToken _prev;
            private int _prevIndex = -1;
            private bool _lastUnary;
            private bool _lastWasComment;
            private bool _forceParenSpace;

            public Writer(string src, List<SqlToken> tokens, FormatOptions o)
            {
                _src = src;
                _t = tokens;
                _o = o;
                _nl = src.Contains("\r\n") || !src.Contains("\n") ? "\r\n" : "\n";
                _nlBefore = new int[tokens.Count];
                for (int i = 0; i < tokens.Count; i++)
                {
                    int from = i == 0 ? 0 : tokens[i - 1].End, count = 0;
                    for (int p = from; p < tokens[i].Start; p++) if (src[p] == '\n') count++;
                    _nlBefore[i] = i == 0 ? Math.Max(count, 1) : count;
                }
            }

            private Scope Top => _stack[_stack.Count - 1];

            public string Run()
            {
                _stack.Add(new Scope { Kind = ScopeKind.Block });
                for (int i = 0; i < _t.Count; i++)
                {
                    if (_t[i].Kind == TokenKind.Comment) { Comment(i); continue; }
                    if (IsGo(i)) { i = Go(i); continue; }
                    Process(ref i);
                }
                FlushComments(-1);
                TrimLineEnd();
                var text = _sb.ToString();
                var tail = _src.TrimEnd(' ', '\t');
                if (tail.EndsWith("\n")) text += _nl;
                return text;
            }

            // ---------------------------------------------------------------- saída

            private void TrimLineEnd()
            {
                int n = _sb.Length;
                while (n > 0 && (_sb[n - 1] == ' ' || _sb[n - 1] == '\t')) n--;
                _sb.Length = n;
            }

            private void NewLine(bool blank)
            {
                TrimLineEnd();
                _sb.Append(_nl);
                if (blank) _sb.Append(_nl);
            }

            private void Indent(int level)
            {
                if (level <= 0) return;
                if (_o.UseTabs) _sb.Append('\t', level);
                else _sb.Append(' ', level * Math.Max(1, _o.IndentSize));
            }

            private void Break(int indent) => _pendingBreak = Math.Max(0, indent);

            private void Write(int i, string text)
            {
                FlushComments(i);
                if (_sb.Length == 0)
                {
                    _lineIndent = _pendingBreak ?? 0;
                    Indent(_lineIndent);
                }
                else if (_pendingBreak != null)
                {
                    NewLine(_nlBefore[i] >= 2);
                    _lineIndent = _pendingBreak.Value;
                    Indent(_lineIndent);
                }
                else if (NeedSpace(i)) _sb.Append(' ');
                _pendingBreak = null;
                _sb.Append(text);
                _prev = _t[i];
                _prevIndex = i;
                _lastUnary = false;
                _lastWasComment = false;
                if (Top.IsStatementScope) Top.AtStart = false;
            }

            private void Comment(int i)
            {
                var c = _t[i];
                bool trailing = _sb.Length > 0 && _nlBefore[i] == 0 && _pendingComments.Count == 0;
                if (!trailing) { _pendingComments.Add(i); return; }
                _sb.Append(' ').Append(c.Text.TrimEnd());
                _lastWasComment = true;
                if (c.Text.StartsWith("--") && _pendingBreak == null) _pendingBreak = _lineIndent;
            }

            private void FlushComments(int next)
            {
                if (_pendingComments.Count == 0) return;
                int indent = _pendingBreak ?? _lineIndent;
                foreach (var c in _pendingComments)
                {
                    if (_sb.Length > 0) NewLine(_nlBefore[c] >= 2);
                    Indent(indent);
                    _sb.Append(_t[c].Text.TrimEnd());
                }
                _lineIndent = indent;
                var last = _t[_pendingComments[_pendingComments.Count - 1]];
                _pendingComments.Clear();
                _lastWasComment = true;
                if (next < 0) return;
                if (last.Text.StartsWith("--") || _nlBefore[next] > 0) _pendingBreak = indent;
                else _pendingBreak = null;
            }

            private bool NeedSpace(int i)
            {
                var cur = _t[i];
                var p = _prev;
                if (p == null) return false;
                if (_lastWasComment) return true;
                if (cur.Kind == TokenKind.Comma || cur.Kind == TokenKind.Semicolon || cur.Kind == TokenKind.CloseParen) return false;
                if (p.Kind == TokenKind.OpenParen || p.Kind == TokenKind.Dot) return false;
                if (cur.Kind == TokenKind.Dot)
                    return !(p.IsName || p.Kind == TokenKind.Variable || p.Kind == TokenKind.CloseParen || p.Kind == TokenKind.Dot);
                // nunca formar "--" ou "/*"
                if (p.Text == "-" && cur.Text == "-") return true;
                if (p.Text == "/" && cur.Kind == TokenKind.Star) return true;
                if (_lastUnary) return false;
                if (cur.Kind == TokenKind.Operator && p.Kind == TokenKind.Operator && cur.Start == p.End) return false;
                if ((cur.Text == ":" || p.Text == ":") && cur.Start == p.End) return false;
                if (p.Text == "$" && cur.Start == p.End) return false; // $action, $IDENTITY
                if (cur.Kind == TokenKind.OpenParen)
                {
                    if (_forceParenSpace) return true;
                    if (p.Kind == TokenKind.Identifier) return IsKeyword(_prevIndex) && !FunctionWords.Contains(p.Text);
                    if (p.Kind == TokenKind.QuotedIdentifier) return false;
                    return true;
                }
                return true;
            }

            // ---------------------------------------------------------------- palavras

            private bool IsKeyword(int i)
            {
                var t = _t[i];
                if (t.Kind != TokenKind.Identifier) return false;
                int p = Prev(i);
                if (p >= 0 && _t[p].Kind == TokenKind.Dot) return false;
                var w = t.Text;
                if (NotReallyReserved.Contains(w))
                {
                    var pw = WordAt(p);
                    switch (w.ToUpperInvariant())
                    {
                        case "APPLY": return pw == "CROSS" || pw == "OUTER";
                        case "MATCHED": return pw == "WHEN" || pw == "NOT";
                        case "GO": return IsGo(i);
                        case "OFFSET":
                            {
                                int n2 = Next(i);
                                return n2 >= 0 && (_t[n2].Kind == TokenKind.Number || _t[n2].Kind == TokenKind.Variable || _t[n2].Kind == TokenKind.OpenParen);
                            }
                        case "OUTPUT":
                            {
                                // cláusula (OUTPUT inserted.Id) ou parâmetro (@x int OUTPUT, @x = @y OUTPUT)
                                var nw2 = WordAt(Next(i));
                                if (nw2 == "INSERTED" || nw2 == "DELETED") return true;
                                if (p < 0) return false;
                                var pk = _t[p].Kind;
                                if (pk == TokenKind.Variable || pk == TokenKind.CloseParen || pk == TokenKind.Number || pk == TokenKind.String || pw == "NULL") return true;
                                int pp = Prev(p);
                                return pk == TokenKind.Identifier && pp >= 0 && _t[pp].Kind == TokenKind.Variable;
                            }
                        case "THROW":
                            {
                                int n2 = Next(i);
                                if (n2 < 0) return true;
                                var k2 = _t[n2].Kind;
                                return k2 == TokenKind.Semicolon || k2 == TokenKind.Number || k2 == TokenKind.Variable || pw == ";" || pw == "CATCH" || pw == "BEGIN";
                            }
                        default: return false;
                    }
                }
                if (SqlKeywords.Reserved.Contains(w) || SafeKeywords.Contains(w)) return true;
                int n = Next(i);
                var before = WordAt(p);
                if (before == "FETCH" && FetchWords.Contains(w)) return true;
                if (string.Equals(w, "NEXT", StringComparison.OrdinalIgnoreCase) && WordAt(n) == "VALUE") return true;
                if (n >= 0 && _t[n].Kind == TokenKind.OpenParen && FunctionWords.Contains(w)) return true;
                if (string.Equals(w, "PARTITION", StringComparison.OrdinalIgnoreCase) && WordAt(n) == "BY") return true;
                return false;
            }

            private string Cased(int i)
            {
                var t = _t[i];
                if (t.Kind != TokenKind.Identifier || _o.KeywordCase == KeywordCase.Keep || !IsKeyword(i)) return t.Text;
                return _o.KeywordCase == KeywordCase.Upper ? t.Text.ToUpperInvariant() : t.Text.ToLowerInvariant();
            }

            private int Next(int i)
            {
                for (int j = i + 1; j < _t.Count; j++) if (_t[j].Kind != TokenKind.Comment) return j;
                return -1;
            }

            private int Prev(int i)
            {
                for (int j = i - 1; j >= 0; j--) if (_t[j].Kind != TokenKind.Comment) return j;
                return -1;
            }

            private string WordAt(int j)
            {
                if (j < 0 || j >= _t.Count) return null;
                return _t[j].Kind == TokenKind.Identifier ? _t[j].Text.ToUpperInvariant() : _t[j].Text;
            }

            private string PrevWord => _prev == null ? null : _prev.Kind == TokenKind.Identifier ? _prev.Text.ToUpperInvariant() : _prev.Text;

            private int MatchParen(int open)
            {
                int depth = 0;
                for (int j = open; j < _t.Count; j++)
                {
                    if (_t[j].Kind == TokenKind.OpenParen) depth++;
                    else if (_t[j].Kind == TokenKind.CloseParen && --depth == 0) return j;
                }
                return -1;
            }

            private int MatchCaseEnd(int start)
            {
                int depth = 0;
                for (int j = start; j < _t.Count; j++)
                {
                    if (_t[j].Is("CASE")) depth++;
                    else if (_t[j].Is("END") && --depth == 0) return j;
                    else if (_t[j].Kind == TokenKind.Semicolon) return -1;
                }
                return -1;
            }

            /// <summary>O trecho [from, to] cabe numa linha (sem comentários e dentro do limite)?</summary>
            private bool Fits(int from, int to)
            {
                if (_o.InlineMaxLength <= 0 || to < from) return false;
                int len = 0;
                for (int j = from; j <= to; j++)
                {
                    if (_t[j].Kind == TokenKind.Comment || _t[j].Kind == TokenKind.Semicolon) return false;
                    if (_t[j].Text.IndexOf('\n') >= 0) return false;
                    len += _t[j].Length + 1;
                    if (len > _o.InlineMaxLength) return false;
                }
                return true;
            }

            private bool IsCte(int withIndex)
            {
                int j = Next(withIndex);
                if (j < 0 || !_t[j].IsName) return false;
                int k = Next(j);
                if (WordAt(k) == "AS") return true;
                if (k < 0 || _t[k].Kind != TokenKind.OpenParen) return false;
                int m = MatchParen(k);
                return m > 0 && WordAt(Next(m)) == "AS";
            }

            private bool IsGo(int i)
            {
                if (!_t[i].Is("GO") || _nlBefore[i] == 0) return false;
                int n = i + 1;
                if (n >= _t.Count || _nlBefore[n] > 0 || _t[n].Kind == TokenKind.Comment) return true;
                if (_t[n].Kind != TokenKind.Number) return false;
                return n + 1 >= _t.Count || _nlBefore[n + 1] > 0 || _t[n + 1].Kind == TokenKind.Comment;
            }

            private int Go(int i)
            {
                _stack.Clear();
                _stack.Add(new Scope { Kind = ScopeKind.Block });
                if (_sb.Length > 0) Break(0);
                Write(i, Cased(i));
                if (i + 1 < _t.Count && _t[i + 1].Kind == TokenKind.Number && _nlBefore[i + 1] == 0)
                {
                    i++;
                    Write(i, _t[i].Text);
                }
                Top.AtStart = true;
                Break(0);
                return i;
            }

            // ---------------------------------------------------------------- despacho

            private void Process(ref int i)
            {
                var t = _t[i];
                var s = Top;

                // parâmetros de procedure sem parênteses: CREATE PROC dbo.p @a int, @b int AS
                if (s.IsStatementScope && t.Kind == TokenKind.Variable && s.Routine && s.Clause == Clause.Header && s.ListFirst < 0 &&
                    _prev != null && _prev.IsName && !IsKeyword(_prevIndex))
                {
                    s.Clause = Clause.ProcParams;
                    BeginList(s, i, true, true);
                }
                if (s.IsStatementScope && s.ListFirst == i) StartList(s, i);

                switch (t.Kind)
                {
                    case TokenKind.OpenParen: OpenParen(i); return;
                    case TokenKind.CloseParen: CloseParen(i); return;
                    case TokenKind.Comma: Comma(i); return;
                    case TokenKind.Semicolon: Semicolon(i); return;
                    case TokenKind.Identifier:
                        if (Word(ref i)) return;
                        break;
                }
                Plain(i);
            }

            private void Plain(int i)
            {
                var t = _t[i];
                bool unary = t.Kind == TokenKind.Operator && (t.Text == "-" || t.Text == "+" || t.Text == "~") && UnaryContext();
                Write(i, t.Kind == TokenKind.Identifier ? Cased(i) : t.Text);
                _lastUnary = unary;
            }

            private bool UnaryContext()
            {
                var p = _prev;
                if (p == null) return true;
                switch (p.Kind)
                {
                    case TokenKind.Operator:
                    case TokenKind.OpenParen:
                    case TokenKind.Comma:
                    case TokenKind.Star:
                    case TokenKind.Semicolon:
                        return true;
                    case TokenKind.Identifier:
                        return IsKeyword(_prevIndex) && !FunctionWords.Contains(p.Text);
                    default:
                        return false;
                }
            }

            /// <param name="alwaysBreak">Quebra um item por linha mesmo que caiba (lista do SELECT, parâmetros).</param>
            private void BeginList(Scope s, int first, bool alwaysBreak, bool asTerminator = false)
            {
                s.ListFirst = first;
                s.ListIndent = s.StmtIndent + 1;
                s.ListIsSelect = alwaysBreak;
                s.ListAsTerminator = asTerminator;
                s.ListBreak = false;
            }

            private void StartList(Scope s, int i)
            {
                s.ListFirst = -1;
                int end;
                int items = CountItems(i, s.ListAsTerminator, out end);
                s.ListBreak = items > 1 && (s.ListIsSelect || !Fits(i, end));
                if (s.ListBreak) Break(s.ListIndent);
            }

            /// <summary>Quantos itens separados por vírgula a lista tem até a próxima cláusula.</summary>
            private int CountItems(int start, bool asTerminator, out int end)
            {
                int depth = 0, caseDepth = 0, items = 1;
                end = start;
                for (int j = start; j < _t.Count; j++)
                {
                    var t = _t[j];
                    if (t.Kind == TokenKind.Comment) continue;
                    if (t.Kind == TokenKind.OpenParen) { depth++; end = j; continue; }
                    if (t.Kind == TokenKind.CloseParen)
                    {
                        if (depth == 0) break;
                        depth--; end = j; continue;
                    }
                    if (depth > 0) { end = j; continue; }
                    if (t.Kind == TokenKind.Semicolon) break;
                    if (t.Kind == TokenKind.Identifier && IsKeyword(j))
                    {
                        if (t.Is("CASE")) caseDepth++;
                        else if (t.Is("END"))
                        {
                            if (caseDepth == 0) break;
                            caseDepth--;
                        }
                        else if (j > start && caseDepth == 0 && (ListTerminators.Contains(t.Text) || (asTerminator && t.Is("AS")))) break;
                    }
                    if (t.Kind == TokenKind.Comma && caseDepth == 0) items++;
                    end = j;
                }
                return items;
            }

            private void Comma(int i)
            {
                var s = Top;
                if (s.Kind == ScopeKind.List) { CommaBreak(i, s.Base); return; }
                if (s.IsStatementScope)
                {
                    if (s.Clause == Clause.With) { Write(i, ","); Break(s.StmtIndent); return; }
                    if (s.ListBreak) { CommaBreak(i, s.ListIndent); return; }
                }
                Write(i, ",");
            }

            private void CommaBreak(int i, int indent)
            {
                if (_o.LeadingCommas) { Break(indent); Write(i, ","); }
                else { Write(i, ","); Break(indent); }
            }

            private void Semicolon(int i)
            {
                Write(i, ";");
                var s = Top;
                if (!s.IsStatementScope) return;
                ResetStatement(s);
                if (!s.AwaitBody) s.SingleDepth = 0;
                s.AtStart = true;
                Break(s.Base + s.SingleDepth);
            }

            private static void ResetStatement(Scope s)
            {
                s.Stmt = Stmt.None;
                s.Clause = Clause.None;
                s.ListBreak = false;
                s.ListFirst = -1;
                s.Between = false;
                s.JoinPhrase = false;
                s.ExpectTableDef = false;
                s.Routine = false;
            }

            private void OpenParen(int i)
            {
                var s = Top;
                int close = MatchParen(i);
                int n = Next(i);
                var nw = WordAt(n);
                var kind = ScopeKind.Inline;
                if (nw == "SELECT" || (nw == "WITH" && IsCte(n))) kind = ScopeKind.Subquery;
                else if (s.IsStatementScope && s.ExpectTableDef) kind = ScopeKind.List;
                else if (s.IsStatementScope && s.Routine && s.Clause == Clause.Header && n >= 0 && _t[n].Kind == TokenKind.Variable)
                    kind = close > 0 && Fits(i, close) ? ScopeKind.Inline : ScopeKind.List;
                if (kind == ScopeKind.Subquery && close > 0 && Fits(i, close)) kind = ScopeKind.Inline;

                _forceParenSpace = kind == ScopeKind.List ||
                                   (s.IsStatementScope && s.Stmt == Stmt.Insert && s.Clause == Clause.InsertTarget && _prev != null && _prev.IsName);
                Write(i, "(");
                _forceParenSpace = false;
                if (s.IsStatementScope) s.ExpectTableDef = false;

                var ns = new Scope { Kind = kind, OpenIndent = _lineIndent, Base = _lineIndent + 1 };
                _stack.Add(ns);
                if (kind != ScopeKind.Inline) Break(ns.Base);
            }

            private void CloseParen(int i)
            {
                int k = _stack.Count - 1;
                while (k > 0 && (_stack[k].Kind == ScopeKind.Case || _stack[k].Kind == ScopeKind.Block)) k--;
                if (k == 0) { Write(i, ")"); return; }
                var ps = _stack[k];
                _stack.RemoveRange(k, _stack.Count - k);
                if (ps.Kind == ScopeKind.Subquery || ps.Kind == ScopeKind.List) Break(ps.OpenIndent);
                Write(i, ")");
            }

            private void OpenCase(int i)
            {
                var s = Top;
                int end = MatchCaseEnd(i);
                bool breaks = (s.IsStatementScope || (s.Kind == ScopeKind.Case && s.Breaks)) && !(end > 0 && Fits(i, end));
                Write(i, Cased(i));
                _stack.Add(new Scope { Kind = ScopeKind.Case, Base = _lineIndent, OpenIndent = _lineIndent, Breaks = breaks });
            }

            /// <summary>Palavra (identificador). Devolve false para tratar como texto comum.</summary>
            private bool Word(ref int i)
            {
                var w = _t[i].Text.ToUpperInvariant();
                var s = Top;

                if (w == "CASE") { OpenCase(i); return true; }
                if (s.Kind == ScopeKind.Case)
                {
                    if (w == "WHEN" || w == "ELSE")
                    {
                        if (s.Breaks) Break(s.Base + 1);
                        Write(i, Cased(i));
                        return true;
                    }
                    if (w == "END")
                    {
                        if (s.Breaks) Break(s.Base);
                        Write(i, Cased(i));
                        _stack.RemoveAt(_stack.Count - 1);
                        return true;
                    }
                    return false;
                }
                if (!s.IsStatementScope) return false;

                var pw = PrevWord;
                int n = Next(i);
                var nw = WordAt(n);

                if (w == "END")
                {
                    if (s.Kind != ScopeKind.Block || _stack.Count < 2) return false;
                    _stack.RemoveAt(_stack.Count - 1);
                    Break(s.OpenIndent);
                    Write(i, Cased(i));
                    if (i + 1 < _t.Count && _t[i + 1].Is("TRY", "CATCH")) { i++; Write(i, Cased(i)); }
                    Top.AtStart = true;
                    return true;
                }

                if (w == "ELSE")
                {
                    int ind = s.StmtIndent;
                    if (s.IfIndents.Count > 0) { ind = s.IfIndents[s.IfIndents.Count - 1]; s.IfIndents.RemoveAt(s.IfIndents.Count - 1); }
                    ResetStatement(s);
                    Break(ind);
                    Write(i, Cased(i));
                    s.Stmt = Stmt.Else;
                    s.StmtIndent = ind;
                    s.SingleDepth = Math.Max(0, ind - s.Base);
                    s.AwaitBody = true;
                    return true;
                }

                var kind = Classify(i, w, pw, nw, s);
                if (kind == Start.None && s.AtStart) kind = Start.New;
                if (kind == Start.New) { StartStatement(ref i, w, pw, nw, s); return true; }
                if (kind == Start.Continuation)
                {
                    Break(s.StmtIndent);
                    Write(i, Cased(i));
                    s.Stmt = StmtOf(w);
                    s.Clause = Clause.None;
                    s.ListBreak = false;
                    s.ListFirst = -1;
                    if (w == "SELECT") { s.Clause = Clause.Select; BeginList(s, FirstSelectItem(i), true); }
                    else if (w == "INSERT") s.Clause = Clause.InsertTarget;
                    return true;
                }
                return ClauseWord(i, w, pw, nw, s);
            }

            private Start Classify(int i, string w, string pw, string nw, Scope s)
            {
                if (_prev != null && (_prev.Kind == TokenKind.Dot || _prev.Kind == TokenKind.Comma || _prev.Kind == TokenKind.Operator)) return Start.None;
                if (!SqlKeywords.StatementStarters.Contains(w) && w != "CHECKPOINT" && w != "KILL") return Start.None;
                if (pw == "GRANT" || pw == "DENY" || pw == "REVOKE") return Start.None;

                switch (w)
                {
                    case "WITH":
                        return IsCte(i) ? Start.New : Start.None;
                    case "SELECT":
                        if (pw == "UNION" || pw == "EXCEPT" || pw == "INTERSECT" || pw == "ALL" || pw == "FOR") return Start.Continuation;
                        if (s.Stmt == Stmt.With && s.Clause == Clause.With) return Start.Continuation;
                        if (s.Stmt == Stmt.Insert && s.Clause != Clause.Values) return Start.Continuation;
                        return Start.New;
                    case "INSERT":
                    case "UPDATE":
                    case "DELETE":
                    case "MERGE":
                        if (w == "UPDATE" && nw == "(") return Start.None;
                        if (pw == "ON" || pw == "FOR" || pw == "OF" || pw == "AFTER" || pw == "INSTEAD" || pw == "THEN" ||
                            pw == "INNER" || pw == "LEFT" || pw == "RIGHT" || pw == "FULL" || pw == "OUTER") return Start.None;
                        if (s.Stmt == Stmt.Merge) return Start.None;
                        if (s.Stmt == Stmt.With && s.Clause == Clause.With) return Start.Continuation;
                        return Start.New;
                    case "SET":
                        if (s.Stmt == Stmt.Update || s.Stmt == Stmt.Merge) return Start.None;
                        if (pw == "DELETE" || pw == "UPDATE") return Start.None;
                        return Start.New;
                    case "EXEC":
                    case "EXECUTE":
                        if (pw == "WITH" || pw == "AS") return Start.None;
                        if (s.Stmt == Stmt.Insert && s.Clause != Clause.Values) return Start.Continuation;
                        return Start.New;
                    case "CREATE":
                    case "ALTER":
                    case "DROP":
                        if (pw == "OR") return Start.None;
                        if ((s.Stmt == Stmt.Alter || s.Stmt == Stmt.Create) && (nw == "COLUMN" || nw == "CONSTRAINT" || nw == "PERIOD")) return Start.None;
                        return Start.New;
                    case "IF":
                        return pw != null && ObjectKinds.Contains(pw) ? Start.None : Start.New;
                    case "FETCH":
                        return s.Clause == Clause.Offset || s.Clause == Clause.OrderBy ? Start.None : Start.New;
                    case "GRANT":
                    case "DENY":
                    case "REVOKE":
                        return pw == "WITH" ? Start.None : Start.New;
                    case "ELSE":
                    case "END":
                        return Start.None;
                    default:
                        return Start.New;
                }
            }

            private static Stmt StmtOf(string w)
            {
                switch (w)
                {
                    case "SELECT": return Stmt.Select;
                    case "INSERT": return Stmt.Insert;
                    case "UPDATE": return Stmt.Update;
                    case "DELETE": return Stmt.Delete;
                    case "MERGE": return Stmt.Merge;
                    case "WITH": return Stmt.With;
                    case "DECLARE": return Stmt.Declare;
                    case "SET": return Stmt.Set;
                    case "EXEC":
                    case "EXECUTE": return Stmt.Exec;
                    case "IF": return Stmt.If;
                    case "WHILE": return Stmt.While;
                    case "CREATE": return Stmt.Create;
                    case "ALTER": return Stmt.Alter;
                    case "DROP": return Stmt.Drop;
                    case "FETCH": return Stmt.Fetch;
                    case "BEGIN": return Stmt.Begin;
                    default: return Stmt.Other;
                }
            }

            private void StartStatement(ref int i, string w, string pw, string nw, Scope s)
            {
                bool block = w == "BEGIN" && !(nw == "TRAN" || nw == "TRANSACTION" || nw == "DISTRIBUTED" || nw == "DIALOG" || nw == "CONVERSATION");
                int indent;
                if (w == "IF" && pw == "ELSE" && s.Stmt == Stmt.Else && s.AwaitBody)
                {
                    // ELSE IF na mesma linha
                    s.AwaitBody = false;
                    indent = _lineIndent;
                }
                else if (s.AwaitBody)
                {
                    s.AwaitBody = false;
                    if (!block) s.SingleDepth++;
                    indent = s.Base + s.SingleDepth;
                    Break(indent);
                }
                else
                {
                    s.SingleDepth = 0;
                    s.IfIndents.Clear();
                    indent = s.Base;
                    Break(indent);
                }

                ResetStatement(s);
                s.StmtIndent = indent;
                s.Stmt = StmtOf(w);
                Write(i, Cased(i));

                switch (w)
                {
                    case "IF":
                        s.IfIndents.Add(indent);
                        s.AwaitBody = true;
                        s.Clause = Clause.Condition;
                        break;
                    case "WHILE":
                        s.AwaitBody = true;
                        s.Clause = Clause.Condition;
                        break;
                    case "BEGIN":
                        if (!block) break;
                        if (i + 1 < _t.Count && _t[i + 1].Is("TRY", "CATCH")) { i++; Write(i, Cased(i)); }
                        _stack.Add(new Scope { Kind = ScopeKind.Block, Base = indent + 1, OpenIndent = indent });
                        Break(indent + 1);
                        break;
                    case "SELECT":
                        s.Clause = Clause.Select;
                        BeginList(s, FirstSelectItem(i), true);
                        break;
                    case "DECLARE":
                        BeginList(s, Next(i), false);
                        break;
                    case "INSERT":
                        s.Clause = Clause.InsertTarget;
                        break;
                    case "WITH":
                        s.Clause = Clause.With;
                        break;
                    case "CREATE":
                    case "ALTER":
                        int j = Next(i);
                        if (WordAt(j) == "OR") j = Next(Next(j));
                        var ow = WordAt(j);
                        s.Routine = ow == "PROC" || ow == "PROCEDURE" || ow == "FUNCTION" || ow == "TRIGGER" || ow == "VIEW";
                        if (s.Routine) s.Clause = Clause.Header;
                        break;
                }
            }

            private int FirstSelectItem(int select)
            {
                int j = Next(select);
                var w = WordAt(j);
                if (w == "DISTINCT" || w == "ALL") { j = Next(j); w = WordAt(j); }
                if (w == "TOP")
                {
                    j = Next(j);
                    if (j >= 0 && _t[j].Kind == TokenKind.OpenParen) { int m = MatchParen(j); j = m < 0 ? -1 : Next(m); }
                    else if (j >= 0) j = Next(j);
                    if (WordAt(j) == "PERCENT") j = Next(j);
                    if (WordAt(j) == "WITH" && WordAt(Next(j)) == "TIES") j = Next(Next(j));
                }
                return j;
            }

            private void ClauseBreak(int i, Scope s, Clause clause)
            {
                Break(s.StmtIndent);
                Write(i, Cased(i));
                s.Clause = clause;
                s.ListBreak = false;
                s.ListFirst = -1;
                s.JoinPhrase = false;
                s.Between = false;
            }

            private bool ClauseWord(int i, string w, string pw, string nw, Scope s)
            {
                bool dml = s.Stmt == Stmt.Select || s.Stmt == Stmt.Update || s.Stmt == Stmt.Delete;
                switch (w)
                {
                    case "FROM":
                        if (!dml || (s.Stmt == Stmt.Delete && s.Clause == Clause.None) || pw == "DISTINCT") return false;
                        ClauseBreak(i, s, Clause.From);
                        return true;
                    case "WHERE":
                        ClauseBreak(i, s, Clause.Where);
                        return true;
                    case "GROUP":
                    case "ORDER":
                        if (nw != "BY" || pw == "WITHIN") return false;
                        ClauseBreak(i, s, w == "GROUP" ? Clause.GroupBy : Clause.OrderBy);
                        BeginList(s, Next(Next(i)), false);
                        return true;
                    case "HAVING":
                        ClauseBreak(i, s, Clause.Having);
                        return true;
                    case "UNION":
                    case "EXCEPT":
                    case "INTERSECT":
                        ClauseBreak(i, s, Clause.Union);
                        return true;
                    case "INTO":
                        if (!((s.Stmt == Stmt.Select && s.Clause == Clause.Select) || s.Clause == Clause.Output)) return false;
                        ClauseBreak(i, s, Clause.Into);
                        return true;
                    case "VALUES":
                        if ((s.Stmt != Stmt.Insert && s.Stmt != Stmt.Merge) || pw == "DEFAULT") return false;
                        ClauseBreak(i, s, Clause.Values);
                        BeginList(s, Next(i), false);
                        return true;
                    case "SET":
                        if ((s.Stmt != Stmt.Update && s.Stmt != Stmt.Merge) || s.Clause == Clause.Set) return false;
                        ClauseBreak(i, s, Clause.Set);
                        BeginList(s, Next(i), false);
                        return true;
                    case "OUTPUT":
                        if (s.Stmt != Stmt.Insert && s.Stmt != Stmt.Update && s.Stmt != Stmt.Delete && s.Stmt != Stmt.Merge) return false;
                        ClauseBreak(i, s, Clause.Output);
                        BeginList(s, Next(i), false);
                        return true;
                    case "OPTION":
                        if (nw != "(") return false;
                        ClauseBreak(i, s, Clause.Other);
                        return true;
                    case "FOR":
                        if (nw != "XML" && nw != "JSON" && nw != "BROWSE") return false;
                        ClauseBreak(i, s, Clause.Other);
                        return true;
                    case "USING":
                        if (s.Stmt != Stmt.Merge) return false;
                        ClauseBreak(i, s, Clause.Join);
                        return true;
                    case "WHEN":
                        if (s.Stmt != Stmt.Merge) return false;
                        ClauseBreak(i, s, Clause.Other);
                        return true;
                    case "ON":
                        if (s.Clause != Clause.Join) return false;
                        Break(s.StmtIndent + 1);
                        Write(i, Cased(i));
                        s.Clause = Clause.On;
                        return true;
                    case "AND":
                    case "OR":
                        if (w == "AND" && s.Between) { s.Between = false; return false; }
                        if (s.Clause != Clause.Where && s.Clause != Clause.Having && s.Clause != Clause.On) return false;
                        Break(s.StmtIndent + 1);
                        Write(i, Cased(i));
                        return true;
                    case "BETWEEN":
                        s.Between = true;
                        return false;
                    case "OFFSET":
                        if (s.Clause != Clause.OrderBy) return false;
                        ClauseBreak(i, s, Clause.Offset);
                        return true;
                    case "RETURNS":
                        if (!s.Routine) return false;
                        ClauseBreak(i, s, Clause.Header);
                        return true;
                    case "AS":
                        if (!s.Routine || (s.Clause != Clause.Header && s.Clause != Clause.ProcParams) || pw == "EXEC" || pw == "EXECUTE") return false;
                        ClauseBreak(i, s, Clause.Body);
                        s.ExpectTableDef = false;
                        s.AtStart = true;
                        return true;
                    case "TABLE":
                        if (s.Stmt == Stmt.Create || s.Stmt == Stmt.Declare || s.Routine) s.ExpectTableDef = true;
                        return false;
                }

                if (IsJoinStart(i, w) && (s.Stmt == Stmt.Select || s.Stmt == Stmt.Update || s.Stmt == Stmt.Delete) &&
                    (s.Clause == Clause.From || s.Clause == Clause.Join || s.Clause == Clause.On))
                {
                    if (s.JoinPhrase)
                    {
                        if (w == "JOIN" || w == "APPLY") s.JoinPhrase = false;
                        return false;
                    }
                    ClauseBreak(i, s, Clause.Join);
                    s.JoinPhrase = w != "JOIN";
                    return true;
                }
                if (s.JoinPhrase && (w == "JOIN" || w == "APPLY")) s.JoinPhrase = false;
                return false;
            }

            private bool IsJoinStart(int i, string w)
            {
                if (w == "JOIN") return true;
                if (w != "INNER" && w != "LEFT" && w != "RIGHT" && w != "FULL" && w != "CROSS" && w != "OUTER") return false;
                int j = i;
                for (int k = 0; k < 4; k++)
                {
                    j = Next(j);
                    var x = WordAt(j);
                    if (x == "JOIN" || x == "APPLY") return true;
                    if (x != "OUTER" && x != "INNER" && x != "LOOP" && x != "HASH" && x != "MERGE" && x != "REMOTE") return false;
                }
                return false;
            }
        }
    }
}
