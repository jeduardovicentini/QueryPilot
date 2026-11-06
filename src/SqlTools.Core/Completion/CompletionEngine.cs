using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SqlTools.Metadata;
using SqlTools.Sql;

namespace SqlTools.Completion
{
    public enum SuggestionKind
    {
        Keyword, Function, Schema, Table, View, TableFunction, ScalarFunction, Procedure, Synonym,
        Column, Variable, Database, Alias, Cte, Join, JoinCondition, ColumnList, Snippet, Parameter
    }

    public sealed class Suggestion
    {
        public string DisplayText { get; set; }
        public string InsertText { get; set; }
        /// <summary>Texto usado para filtrar pelo que foi digitado (padrão: DisplayText).</summary>
        public string FilterText { get; set; }
        public SuggestionKind Kind { get; set; }
        public string Description { get; set; }
        /// <summary>Texto curto à direita do item (ex.: tipo da coluna).</summary>
        public string Detail { get; set; }
        /// <summary>Menor aparece antes.</summary>
        public int Priority { get; set; }
        /// <summary>Quando definido, substitui este trecho em vez da palavra sob o cursor (ex.: expandir *).</summary>
        public int? ReplaceStart { get; set; }
        public int ReplaceLength { get; set; }

        public override string ToString() => Kind + ":" + DisplayText;
    }

    public sealed class TextEdit
    {
        public int Start { get; set; }
        public int Length { get; set; }
        public string Text { get; set; }
        /// <summary>A edição principal (o * virou a lista de colunas).</summary>
        public bool IsColumns { get; set; }
        /// <summary>Trecho a selecionar depois de aplicar, relativo a <see cref="Start"/> (-1 = nenhum).</summary>
        public int SelectOffset { get; set; } = -1;
        public int SelectLength { get; set; }
    }

    public sealed class CompletionOptions
    {
        /// <summary>Ao escolher uma tabela no FROM/JOIN, insere também um alias curto.</summary>
        public bool AutoAlias { get; set; } = true;
    }

    /// <summary>Tabela do escopo com o objeto do catálogo (ou as colunas da CTE/subconsulta).</summary>
    public sealed class ResolvedTable
    {
        public TableRef Ref { get; set; }
        public DbObject Object { get; set; }
        public List<DbColumn> Columns { get; } = new List<DbColumn>();
        public string RefName => Ref.RefName;
        public string Display => Object != null ? Object.FullName : Ref.Name ?? Ref.Alias;
    }

    /// <summary>Monta a lista de sugestões para um contexto. Sem dependência do Visual Studio (testável).</summary>
    public static class CompletionEngine
    {
        private static readonly string[] SystemSchemas = { "sys", "INFORMATION_SCHEMA" };
        private static readonly Regex RegularIdentifier = new Regex(@"^[A-Za-z_#@][A-Za-z0-9_@#$]*$", RegexOptions.Compiled);

        public static List<Suggestion> GetSuggestions(SqlCompletionContext ctx, DatabaseMetadata md, CompletionOptions opt = null)
        {
            opt = opt ?? new CompletionOptions();
            var list = new List<Suggestion>();
            var scope = Resolve(ctx, md);

            switch (ctx.Kind)
            {
                case ContextKind.StatementStart:
                    AddSnippets(list, inStatementOnly: false);
                    AddKeywords(list);
                    break;

                case ContextKind.Expression:
                    AddExpression(list, ctx, scope);
                    AddSnippets(list, inStatementOnly: true);
                    break;

                case ContextKind.AfterTableRef:
                    AddSnippets(list, inStatementOnly: true);
                    AddKeywords(list);
                    break;

                case ContextKind.ProcParameters:
                    AddProcParameters(list, ctx, md, all: true);
                    break;

                case ContextKind.ObjectName:
                    if (ctx.IsJoin && md != null) AddJoins(list, scope, md);
                    AddCtes(list, ctx, opt);
                    AddLocalTables(list, ctx, opt);
                    if (md != null) AddObjects(list, ctx, md, null, opt);
                    if (md != null) AddSchemas(list, md);
                    if (ctx.ObjectKeyword == "DELETE") list.Add(Kw("FROM", 1));
                    if (ctx.ObjectKeyword == "INSERT") list.Add(Kw("INTO", 1));
                    break;

                case ContextKind.Procedure:
                    if (md != null)
                    {
                        AddProcedures(list, md, null);
                        AddSchemas(list, md);
                    }
                    break;

                case ContextKind.Database:
                    if (md != null)
                        foreach (var db in md.Databases)
                            list.Add(new Suggestion { DisplayText = db, InsertText = Quote(db), Kind = SuggestionKind.Database, Priority = 10, Description = "Banco de dados" });
                    break;

                case ContextKind.Member:
                    AddMembers(list, ctx, scope, md, opt);
                    break;

                case ContextKind.JoinCondition:
                    if (md != null) AddJoinConditions(list, ctx, scope, md);
                    AddExpression(list, ctx, scope);
                    break;

                case ContextKind.InsertColumns:
                    AddInsertColumns(list, ctx, md, inParens: true);
                    break;

                case ContextKind.AfterInsertTarget:
                    AddInsertColumns(list, ctx, md, inParens: false);
                    foreach (var k in new[] { "VALUES", "SELECT", "DEFAULT VALUES", "EXEC", "OUTPUT" }) list.Add(Kw(k, 30));
                    break;

                case ContextKind.Variable:
                    if (ctx.ProcedureName.Count > 0) AddProcParameters(list, ctx, md, all: false);
                    foreach (var v in ctx.Variables)
                        list.Add(new Suggestion { DisplayText = v, InsertText = v, Kind = SuggestionKind.Variable, Priority = 0, Description = "Variável" });
                    foreach (var v in new[] { "@@ROWCOUNT", "@@ERROR", "@@IDENTITY", "@@TRANCOUNT", "@@SPID", "@@VERSION", "@@SERVERNAME", "@@FETCH_STATUS" })
                        list.Add(new Suggestion { DisplayText = v, InsertText = v, Kind = SuggestionKind.Function, Priority = 20, Description = "Função de sistema" });
                    break;

                case ContextKind.StarExpansion:
                    AddStarExpansion(list, ctx, scope);
                    break;
            }

            foreach (var s in list)
            {
                if (s.InsertText == null) s.InsertText = s.DisplayText;
                if (s.FilterText == null) s.FilterText = s.DisplayText;
            }
            return list;
        }

        // ---------- escopo ----------

        public static List<ResolvedTable> Resolve(SqlCompletionContext ctx, DatabaseMetadata md)
        {
            var result = new List<ResolvedTable>();
            foreach (var r in ctx.Tables)
            {
                var rt = new ResolvedTable { Ref = r };
                var cte = r.Parts.Count == 1 ? ctx.Ctes.FirstOrDefault(c => Eq(c.Name, r.Name)) : null;
                var local = r.Parts.Count >= 1 && r.Name != null && (r.Name.StartsWith("#") || r.Name.StartsWith("@"))
                    ? ctx.LocalTables.FirstOrDefault(l => Eq(l.Name, r.Name)) : null;
                if (local != null)
                    rt.Columns.AddRange(local.DerivedColumns.Select((c, i) => new DbColumn { Name = c, Ordinal = i + 1, DataType = i < local.DerivedTypes.Count ? local.DerivedTypes[i] : null }));
                else if (r.IsDerived)
                    rt.Columns.AddRange(r.DerivedColumns.Select((c, i) => new DbColumn { Name = c, Ordinal = i + 1 }));
                else if (cte != null)
                    rt.Columns.AddRange(cte.DerivedColumns.Select((c, i) => new DbColumn { Name = c, Ordinal = i + 1 }));
                else if (md != null && (r.Database == null || Eq(r.Database, md.Database)))
                {
                    rt.Object = md.Find(r.Schema, r.Name);
                    if (rt.Object != null) rt.Columns.AddRange(rt.Object.Columns.OrderBy(c => c.Ordinal));
                }
                result.Add(rt);
            }
            return result;
        }

        // ---------- blocos de sugestões ----------

        private static void AddKeywords(List<Suggestion> list, int priority = 40)
        {
            foreach (var k in SqlKeywords.Suggested) list.Add(Kw(k, priority));
        }

        private static Suggestion Kw(string k, int priority) =>
            new Suggestion { DisplayText = k, InsertText = k, Kind = SuggestionKind.Keyword, Priority = priority, Description = "Palavra-chave" };

        private static void AddExpression(List<Suggestion> list, SqlCompletionContext ctx, List<ResolvedTable> scope)
        {
            // colunas: nome simples; se o mesmo nome existe em mais de uma tabela do escopo, alias.coluna
            var counts = scope.SelectMany(t => t.Columns.Select(c => c.Name)).GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var t in scope)
            {
                foreach (var c in t.Columns)
                {
                    bool ambiguous = counts[c.Name] > 1;
                    var name = Quote(c.Name);
                    list.Add(new Suggestion
                    {
                        DisplayText = ambiguous ? t.RefName + "." + c.Name : c.Name,
                        InsertText = ambiguous ? Quote(t.RefName) + "." + name : name,
                        FilterText = c.Name,
                        Kind = SuggestionKind.Column,
                        Priority = 10,
                        Detail = c.DataType,
                        Description = ColumnDescription(t, c)
                    });
                }
            }
            foreach (var t in scope.Where(t => !string.IsNullOrEmpty(t.RefName)).GroupBy(t => t.RefName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                list.Add(new Suggestion { DisplayText = t.RefName, InsertText = Quote(t.RefName), Kind = SuggestionKind.Alias, Priority = 12, Description = "Alias de " + t.Display });
            foreach (var v in ctx.Variables)
                list.Add(new Suggestion { DisplayText = v, InsertText = v, Kind = SuggestionKind.Variable, Priority = 15, Description = "Variável" });
            foreach (var f in SqlKeywords.Functions)
                list.Add(new Suggestion { DisplayText = f.Key, InsertText = f.Key, Kind = SuggestionKind.Function, Priority = 30, Description = f.Value });
            AddKeywords(list);
        }

        private static void AddSnippets(List<Suggestion> list, bool inStatementOnly)
        {
            foreach (var sn in SqlSnippets.BuiltIn.Where(x => !inStatementOnly || x.InStatement))
                list.Add(new Suggestion
                {
                    DisplayText = sn.Shortcut,
                    InsertText = sn.Text,
                    FilterText = sn.Shortcut,
                    Kind = SuggestionKind.Snippet,
                    Priority = 35,
                    Detail = sn.Description,
                    Description = "Snippet " + sn.Shortcut + " (Tab)\n" + sn.Text
                });
        }

        /// <summary>Tabelas temporárias e variáveis de tabela no FROM/JOIN.</summary>
        private static void AddLocalTables(List<Suggestion> list, SqlCompletionContext ctx, CompletionOptions opt)
        {
            foreach (var l in ctx.LocalTables)
            {
                var insert = l.Name;
                if (WantsAlias(ctx, opt)) insert += " " + AliasGenerator.For(l.Name, UsedAliases(ctx));
                list.Add(new Suggestion
                {
                    DisplayText = l.Name,
                    InsertText = insert,
                    FilterText = l.Name,
                    Kind = SuggestionKind.Table,
                    Priority = 6,
                    Description = (l.Name.StartsWith("@") ? "Variável de tabela" : "Tabela temporária") + " (" + string.Join(", ", l.DerivedColumns) + ")"
                });
            }
        }

        /// <summary>Parâmetros da procedure do EXEC (os ainda não informados) e, no começo da lista, todos de uma vez.</summary>
        private static void AddProcParameters(List<Suggestion> list, SqlCompletionContext ctx, DatabaseMetadata md, bool all)
        {
            if (md == null || ctx.ProcedureName.Count == 0) return;
            var name = ctx.ProcedureName[ctx.ProcedureName.Count - 1];
            var schema = ctx.ProcedureName.Count >= 2 ? ctx.ProcedureName[ctx.ProcedureName.Count - 2] : null;
            var proc = md.Find(schema, name);
            if (proc == null || proc.Kind != DbObjectKind.Procedure) return;
            var pending = proc.Parameters.OrderBy(p => p.Ordinal)
                .Where(p => !ctx.UsedParameters.Any(u => Eq(u, p.Name))).ToList();
            if (pending.Count == 0) return;

            if (all && ctx.UsedParameters.Count == 0 && pending.Count > 1)
            {
                var text = string.Join(", ", pending.Select(p => p.Name + " = " + (p.IsOutput ? p.Name + " OUTPUT" : "NULL")));
                list.Add(new Suggestion
                {
                    DisplayText = "(todos os parâmetros)",
                    InsertText = text,
                    FilterText = "@",
                    Kind = SuggestionKind.ColumnList,
                    Priority = 0,
                    Description = text
                });
            }
            foreach (var p in pending)
                list.Add(new Suggestion
                {
                    DisplayText = p.Name,
                    InsertText = p.Name,
                    Kind = SuggestionKind.Parameter,
                    Priority = 1,
                    Detail = p.DataType + (p.IsOutput ? " OUTPUT" : ""),
                    Description = "Parâmetro de " + proc.FullName + ": " + p.Name + " " + p.DataType + (p.IsOutput ? " OUTPUT" : "")
                });
        }

        private static void AddCtes(List<Suggestion> list, SqlCompletionContext ctx, CompletionOptions opt)
        {
            foreach (var c in ctx.Ctes)
            {
                var insert = Quote(c.Name);
                if (WantsAlias(ctx, opt)) insert += " " + AliasGenerator.For(c.Name, UsedAliases(ctx));
                list.Add(new Suggestion { DisplayText = c.Name, InsertText = insert, FilterText = c.Name, Kind = SuggestionKind.Cte, Priority = 5, Description = "CTE (" + string.Join(", ", c.DerivedColumns) + ")" });
            }
        }

        private static bool WantsAlias(SqlCompletionContext ctx, CompletionOptions opt) =>
            opt.AutoAlias && (ctx.ObjectKeyword == "FROM" || ctx.ObjectKeyword == "JOIN" || ctx.ObjectKeyword == "APPLY" || ctx.ObjectKeyword == "USING");

        private static List<string> UsedAliases(SqlCompletionContext ctx) =>
            ctx.Tables.Select(t => t.RefName).Where(a => a != null).ToList();

        /// <summary>Tabelas, views e funções de tabela. Com schema: só as dele; sem schema: menos as de sistema.</summary>
        private static void AddObjects(List<Suggestion> list, SqlCompletionContext ctx, DatabaseMetadata md, string schema, CompletionOptions opt)
        {
            var alias = WantsAlias(ctx, opt);
            var used = UsedAliases(ctx);
            foreach (var o in md.AllObjects.Where(o => o.IsTableLike))
            {
                if (schema != null ? !Eq(o.Schema, schema) : SystemSchemas.Any(s => Eq(s, o.Schema))) continue;
                var showSchema = schema == null && !Eq(o.Schema, md.DefaultSchema);
                var display = showSchema ? o.Schema + "." + o.Name : o.Name;
                var insert = (showSchema ? Quote(o.Schema) + "." : "") + Quote(o.Name);
                if (o.Kind == DbObjectKind.TableFunction) insert += "()";
                if (alias) insert += " " + AliasGenerator.For(o.Name, used);
                list.Add(new Suggestion
                {
                    DisplayText = display,
                    InsertText = insert,
                    FilterText = o.Name,
                    Kind = KindOf(o),
                    Priority = o.Kind == DbObjectKind.Table ? 10 : 11,
                    Description = ObjectDescription(o)
                });
            }
        }

        private static void AddProcedures(List<Suggestion> list, DatabaseMetadata md, string schema)
        {
            foreach (var o in md.AllObjects.Where(o => o.Kind == DbObjectKind.Procedure))
            {
                bool system = SystemSchemas.Any(s => Eq(s, o.Schema));
                if (schema != null) { if (!Eq(o.Schema, schema)) continue; }
                else if (system && !o.Name.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)) continue;
                var showSchema = schema == null && !system && !Eq(o.Schema, md.DefaultSchema);
                list.Add(new Suggestion
                {
                    DisplayText = showSchema ? o.Schema + "." + o.Name : o.Name,
                    InsertText = (showSchema ? Quote(o.Schema) + "." : "") + Quote(o.Name),
                    FilterText = o.Name,
                    Kind = SuggestionKind.Procedure,
                    Priority = system ? 15 : 10,
                    Description = ObjectDescription(o)
                });
            }
        }

        private static void AddSchemas(List<Suggestion> list, DatabaseMetadata md)
        {
            foreach (var s in md.Schemas)
                list.Add(new Suggestion { DisplayText = s, InsertText = Quote(s), Kind = SuggestionKind.Schema, Priority = 20, Description = "Schema" });
        }

        private static void AddMembers(List<Suggestion> list, SqlCompletionContext ctx, List<ResolvedTable> scope, DatabaseMetadata md, CompletionOptions opt)
        {
            var q = ctx.Qualifier[ctx.Qualifier.Count - 1];

            if (ctx.QualifierPosition == ContextKind.Expression)
            {
                // alias.coluna (o alias tem preferência sobre um schema com o mesmo nome)
                var t = scope.FirstOrDefault(x => Eq(x.RefName, q)) ?? scope.FirstOrDefault(x => Eq(x.Ref.Name, q));
                if (t != null)
                {
                    foreach (var c in t.Columns)
                        list.Add(new Suggestion { DisplayText = c.Name, InsertText = Quote(c.Name), Kind = SuggestionKind.Column, Priority = c.IsPrimaryKey ? 9 : 10, Detail = c.DataType, Description = ColumnDescription(t, c) });
                    return;
                }
            }

            if (md == null) return;
            // banco.schema. → só o banco atual está carregado
            if (ctx.Qualifier.Count >= 2 && !string.IsNullOrEmpty(ctx.Qualifier[ctx.Qualifier.Count - 2]) && !Eq(ctx.Qualifier[ctx.Qualifier.Count - 2], md.Database))
                return;
            if (ctx.Qualifier.Count >= 2 && string.IsNullOrEmpty(q)) q = md.DefaultSchema; // banco..tabela
            if (!md.Schemas.Any(s => Eq(s, q))) return;

            if (ctx.QualifierPosition == ContextKind.Procedure)
            {
                AddProcedures(list, md, q);
                return;
            }
            AddObjects(list, ctx, md, q, opt);
            if (ctx.QualifierPosition == ContextKind.Expression)
            {
                foreach (var o in md.AllObjects.Where(o => o.Kind == DbObjectKind.ScalarFunction && Eq(o.Schema, q)))
                    list.Add(new Suggestion { DisplayText = o.Name, InsertText = Quote(o.Name), Kind = SuggestionKind.ScalarFunction, Priority = 12, Description = ObjectDescription(o) });
            }
        }

        /// <summary>JOIN completo a partir das FKs entre as tabelas já no FROM e as demais.</summary>
        private static void AddJoins(List<Suggestion> list, List<ResolvedTable> scope, DatabaseMetadata md)
        {
            var used = scope.Select(t => t.RefName).Where(a => a != null).ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in scope.Where(t => t.Object != null))
            {
                foreach (var fk in md.ForeignKeysOf(s.Object))
                {
                    bool sIsFrom = fk.From == s.Object;
                    var other = sIsFrom ? fk.To : fk.From;
                    var alias = AliasGenerator.For(other.Name, used);
                    var cond = string.Join(" AND ", fk.Columns.Select(p => sIsFrom
                        ? alias + "." + Quote(p.Value) + " = " + Quote(s.RefName) + "." + Quote(p.Key)
                        : alias + "." + Quote(p.Key) + " = " + Quote(s.RefName) + "." + Quote(p.Value)));
                    var name = (Eq(other.Schema, md.DefaultSchema) ? "" : Quote(other.Schema) + ".") + Quote(other.Name);
                    var text = name + " " + alias + " ON " + cond;
                    if (!seen.Add(text)) continue;
                    list.Add(new Suggestion
                    {
                        DisplayText = text,
                        InsertText = text,
                        FilterText = other.Name,
                        Kind = SuggestionKind.Join,
                        Priority = 0,
                        Description = "JOIN pela FK " + fk.Name + "\n" + fk.From.FullName + " → " + fk.To.FullName
                    });
                }
            }
        }

        /// <summary>Condições do ON: FKs entre a tabela do JOIN e as anteriores; senão, colunas de mesmo nome que são PK.</summary>
        private static void AddJoinConditions(List<Suggestion> list, SqlCompletionContext ctx, List<ResolvedTable> scope, DatabaseMetadata md)
        {
            var target = scope.FirstOrDefault(t => t.Ref == ctx.JoinTarget);
            if (target == null) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in scope.Where(t => t != target && t.Ref.Position < target.Ref.Position))
            {
                if (target.Object != null && s.Object != null)
                {
                    foreach (var fk in md.ForeignKeys.Where(f => (f.From == target.Object && f.To == s.Object) || (f.From == s.Object && f.To == target.Object)))
                    {
                        bool tIsFrom = fk.From == target.Object;
                        var cond = string.Join(" AND ", fk.Columns.Select(p => tIsFrom
                            ? Quote(target.RefName) + "." + Quote(p.Key) + " = " + Quote(s.RefName) + "." + Quote(p.Value)
                            : Quote(target.RefName) + "." + Quote(p.Value) + " = " + Quote(s.RefName) + "." + Quote(p.Key)));
                        if (seen.Add(cond))
                            list.Add(new Suggestion { DisplayText = cond, InsertText = cond, FilterText = target.RefName + " " + cond, Kind = SuggestionKind.JoinCondition, Priority = 0, Description = "FK " + fk.Name });
                    }
                }
                foreach (var c in target.Columns)
                {
                    var other = s.Columns.FirstOrDefault(x => Eq(x.Name, c.Name));
                    if (other == null || !(c.IsPrimaryKey || other.IsPrimaryKey)) continue;
                    var cond = Quote(target.RefName) + "." + Quote(c.Name) + " = " + Quote(s.RefName) + "." + Quote(other.Name);
                    if (seen.Add(cond))
                        list.Add(new Suggestion { DisplayText = cond, InsertText = cond, FilterText = target.RefName + " " + cond, Kind = SuggestionKind.JoinCondition, Priority = 2, Description = "Colunas com o mesmo nome" });
                }
            }
        }

        private static void AddInsertColumns(List<Suggestion> list, SqlCompletionContext ctx, DatabaseMetadata md, bool inParens)
        {
            if (md == null || ctx.InsertTarget == null) return;
            var obj = md.Find(ctx.InsertTarget.Schema, ctx.InsertTarget.Name);
            if (obj == null) return;
            var cols = obj.Columns.OrderBy(c => c.Ordinal).ToList();
            var insertable = cols.Where(c => c.Insertable).Select(c => Quote(c.Name)).ToList();
            if (insertable.Count > 0)
            {
                var all = string.Join(", ", insertable);
                list.Add(new Suggestion
                {
                    DisplayText = inParens ? "(todas as colunas)" : "(" + all + ")",
                    InsertText = inParens ? all : "(" + all + ")",
                    FilterText = inParens ? "*" : "(",
                    Kind = SuggestionKind.ColumnList,
                    Priority = 0,
                    Description = "Colunas de " + obj.FullName + " que aceitam valor (sem identity, calculadas e rowversion)"
                });
            }
            if (!inParens) return;
            foreach (var c in cols)
                list.Add(new Suggestion { DisplayText = c.Name, InsertText = Quote(c.Name), Kind = SuggestionKind.Column, Priority = c.Insertable ? 10 : 20, Detail = c.DataType, Description = c.DataType + (c.Insertable ? "" : " (não aceita valor)") });
        }

        /// <summary>
        /// Tab no *: troca o * (ou alias.*) sob o cursor — logo antes ou logo depois dele — pelas colunas qualificadas
        /// com o alias, e cria o alias nas tabelas que não têm. Devolve as edições do fim para o começo, ou null.
        /// </summary>
        public static List<TextEdit> ExpandStar(string text, int caret, DatabaseMetadata md)
        {
            if (string.IsNullOrEmpty(text) || md == null || caret < 0 || caret > text.Length) return null;
            int after;
            if (caret < text.Length && text[caret] == '*') after = caret + 1;
            else if (caret > 0 && text[caret - 1] == '*') after = caret;
            else return null;

            var ctx = SqlContextAnalyzer.Analyze(text, after);
            if (ctx.Kind != ContextKind.StarExpansion) return null;
            var scope = Resolve(ctx, md);
            var tables = (ctx.StarQualifier != null
                ? scope.Where(t => Eq(t.RefName, ctx.StarQualifier) || Eq(t.Ref.Name, ctx.StarQualifier)).Take(1)
                : scope).Where(t => t.Columns.Count > 0).ToList();
            if (tables.Count == 0) return null;

            var edits = new List<TextEdit>();
            var used = UsedAliases(ctx);
            var cols = new List<string>();
            List<SqlToken> stmt = null;
            foreach (var t in tables)
            {
                var alias = t.Ref.Alias;
                if (string.IsNullOrEmpty(alias))
                {
                    alias = AliasGenerator.For(t.Ref.Name, used);
                    used.Add(alias);
                    edits.Add(new TextEdit { Start = t.Ref.NameEnd, Length = 0, Text = " " + alias });
                    // com alias, "Tabela.coluna" deixa de valer: troca por "alias.coluna" na mesma instrução
                    stmt = stmt ?? StatementTokens(text, ctx.StarStart);
                    for (int i = 0; i + 1 < stmt.Count; i++)
                    {
                        var k = stmt[i];
                        if (!k.IsName || !Eq(k.Value, t.Ref.Name) || stmt[i + 1].Kind != TokenKind.Dot) continue;
                        if (k.Start >= ctx.StarStart && k.Start < ctx.StarStart + ctx.StarLength) continue;
                        if (i + 2 < stmt.Count && stmt[i + 2].IsName && i + 3 < stmt.Count && stmt[i + 3].Kind == TokenKind.Dot) continue; // é schema
                        int from = k.Start;
                        if (i > 0 && stmt[i - 1].Kind == TokenKind.Dot)
                        {
                            // schema.Tabela.coluna → alias.coluna
                            if (i < 2 || !stmt[i - 2].IsName || (i >= 3 && stmt[i - 3].Kind == TokenKind.Dot)) continue;
                            from = stmt[i - 2].Start;
                        }
                        edits.Add(new TextEdit { Start = from, Length = k.End - from, Text = alias });
                    }
                }
                cols.AddRange(t.Columns.Select(c => Quote(alias) + "." + Quote(c.Name)));
            }
            edits.Add(new TextEdit { Start = ctx.StarStart, Length = ctx.StarLength, Text = string.Join(", ", cols), IsColumns = true });
            return edits.OrderByDescending(e => e.Start).ToList();
        }

        /// <summary>
        /// Tab no fim de "INSERT [INTO] tabela": troca pelo INSERT completo no layout do "Script as INSERT" do SSMS,
        /// com as colunas que aceitam valor e um marcador &lt;Coluna&gt; para cada valor. O primeiro marcador fica selecionado.
        /// </summary>
        public static TextEdit ExpandInsert(string text, int caret, DatabaseMetadata md)
        {
            if (string.IsNullOrEmpty(text) || md == null || caret <= 0 || caret > text.Length) return null;
            // depois do cursor, só espaço até o fim da linha
            for (int p = caret; p < text.Length && text[p] != '\n' && text[p] != '\r'; p++)
                if (!char.IsWhiteSpace(text[p])) return null;

            var t = SqlTokenizer.Tokenize(text.Substring(0, caret)).Where(x => x.Kind != TokenKind.Comment).ToList();
            if (t.Count < 2 || !t[t.Count - 1].IsName) return null;
            for (int p = t[t.Count - 1].End; p < caret; p++) if (text[p] != ' ' && text[p] != '\t') return null;

            // nome com pontos, do fim para o começo
            int i = t.Count - 1;
            var parts = new List<string> { t[i].Value };
            while (i >= 2 && t[i - 1].Kind == TokenKind.Dot && t[i - 2].IsName) { parts.Insert(0, t[i - 2].Value); i -= 2; }
            int kw = i - 1;
            if (kw >= 0 && t[kw].Is("INTO")) kw--;
            if (kw < 0 || !t[kw].Is("INSERT")) return null;
            if (kw > 0 && (t[kw - 1].Kind == TokenKind.Comma || t[kw - 1].Is("THEN", "FOR", "AFTER", "OF", "ON"))) return null;

            var name = parts[parts.Count - 1];
            var schema = parts.Count >= 2 ? parts[parts.Count - 2] : null;
            var obj = md.Find(schema, name);
            if (obj == null || !(obj.Kind == DbObjectKind.Table || obj.Kind == DbObjectKind.View)) return null;
            var cols = obj.Columns.Where(c => c.Insertable).OrderBy(c => c.Ordinal).ToList();
            if (cols.Count == 0) return null;

            int start = t[kw].Start;
            int lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
            var lead = text.Substring(lineStart, start - lineStart);
            var b = lead.Trim().Length == 0 ? lead : "";
            var nl = text.Contains("\r\n") || !text.Contains("\n") ? "\r\n" : "\n";
            var pad = b + new string(' ', 11);

            var sb = new System.Text.StringBuilder();
            sb.Append("INSERT INTO ").Append(Bracket(obj.Schema)).Append('.').Append(Bracket(obj.Name));
            for (int c = 0; c < cols.Count; c++)
                sb.Append(nl).Append(pad).Append(c == 0 ? "(" : ",").Append(Bracket(cols[c].Name)).Append(c == cols.Count - 1 ? ")" : "");
            sb.Append(nl).Append(b).Append("     VALUES");
            int select = -1;
            for (int c = 0; c < cols.Count; c++)
            {
                sb.Append(nl).Append(pad).Append(c == 0 ? "(" : ",");
                if (c == 0) select = sb.Length;
                sb.Append('<').Append(cols[c].Name).Append('>').Append(c == cols.Count - 1 ? ")" : "");
            }
            return new TextEdit
            {
                Start = start,
                Length = caret - start,
                Text = sb.ToString(),
                SelectOffset = select,
                SelectLength = cols[0].Name.Length + 2
            };
        }

        private static string Bracket(string name) => "[" + name.Replace("]", "]]") + "]";

        /// <summary>Tokens (sem comentários) da instrução que contém <paramref name="pos"/>.</summary>
        private static List<SqlToken> StatementTokens(string text, int pos)
        {
            var t = SqlTokenizer.Tokenize(text).Where(x => x.Kind != TokenKind.Comment).ToList();
            var starts = SqlContextAnalyzer.SegmentStatements(t);
            int at = t.FindIndex(x => x.End > pos);
            if (at < 0) return new List<SqlToken>();
            return t.Where((x, i) => starts[i] == starts[at]).ToList();
        }

        private static void AddStarExpansion(List<Suggestion> list, SqlCompletionContext ctx, List<ResolvedTable> scope)
        {
            var tables = ctx.StarQualifier != null
                ? scope.Where(t => Eq(t.RefName, ctx.StarQualifier) || Eq(t.Ref.Name, ctx.StarQualifier)).Take(1).ToList()
                : scope;
            tables = tables.Where(t => t.Columns.Count > 0).ToList();
            if (tables.Count == 0) return;
            bool qualify = ctx.StarQualifier != null || tables.Count > 1;
            var cols = tables.SelectMany(t => t.Columns.Select(c => (qualify ? Quote(t.RefName) + "." : "") + Quote(c.Name))).ToList();
            list.Add(new Suggestion
            {
                DisplayText = "* → " + cols.Count + " colunas",
                InsertText = string.Join(", ", cols),
                FilterText = "*",
                Kind = SuggestionKind.ColumnList,
                Priority = 0,
                Description = "Troca o * pela lista de colunas de " + string.Join(", ", tables.Select(t => t.Display)),
                ReplaceStart = ctx.StarStart,
                ReplaceLength = ctx.StarLength
            });
        }

        // ---------- textos ----------

        private static string ColumnDescription(ResolvedTable t, DbColumn c)
        {
            var flags = new List<string>();
            if (c.IsPrimaryKey) flags.Add("PK");
            if (c.IsIdentity) flags.Add("identity");
            if (c.IsComputed) flags.Add("calculada");
            var type = string.IsNullOrEmpty(c.DataType) ? "" : " " + c.DataType + (c.Nullable ? " NULL" : " NOT NULL");
            return t.Display + "." + c.Name + type + (flags.Count > 0 ? " (" + string.Join(", ", flags) + ")" : "");
        }

        public static string ObjectDescription(DbObject o)
        {
            string kind;
            switch (o.Kind)
            {
                case DbObjectKind.Table: kind = "Tabela"; break;
                case DbObjectKind.View: kind = "View"; break;
                case DbObjectKind.Procedure: kind = "Procedure"; break;
                case DbObjectKind.ScalarFunction: kind = "Função escalar"; break;
                case DbObjectKind.TableFunction: kind = "Função de tabela"; break;
                default: kind = "Sinônimo"; break;
            }
            var head = kind + " " + o.FullName;
            if (o.Parameters.Count > 0)
                head += "(" + string.Join(", ", o.Parameters.OrderBy(p => p.Ordinal).Select(p => p.Name + " " + p.DataType + (p.IsOutput ? " OUTPUT" : ""))) + ")";
            if (o.Columns.Count == 0) return head;
            var cols = o.Columns.OrderBy(c => c.Ordinal).Take(12).Select(c => "  " + c.Name + " " + c.DataType + (c.IsPrimaryKey ? " PK" : ""));
            return head + "\n" + string.Join("\n", cols) + (o.Columns.Count > 12 ? "\n  … +" + (o.Columns.Count - 12) + " colunas" : "");
        }

        private static SuggestionKind KindOf(DbObject o)
        {
            switch (o.Kind)
            {
                case DbObjectKind.Table: return SuggestionKind.Table;
                case DbObjectKind.View: return SuggestionKind.View;
                case DbObjectKind.TableFunction: return SuggestionKind.TableFunction;
                case DbObjectKind.ScalarFunction: return SuggestionKind.ScalarFunction;
                case DbObjectKind.Procedure: return SuggestionKind.Procedure;
                default: return SuggestionKind.Synonym;
            }
        }

        /// <summary>Põe entre colchetes nomes que não são identificadores regulares ou que são palavras reservadas.</summary>
        public static string Quote(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (RegularIdentifier.IsMatch(name) && !SqlKeywords.Reserved.Contains(name)) return name;
            return "[" + name.Replace("]", "]]") + "]";
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
