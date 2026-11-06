using System;
using System.Collections.Generic;
using System.Linq;
using SqlTools.Metadata;
using SqlTools.Sql;

namespace SqlTools.Completion
{
    public sealed class SymbolInfo
    {
        /// <summary>Objeto do catálogo (a tabela, no caso de coluna ou alias).</summary>
        public DbObject Object { get; set; }
        /// <summary>Preenchida quando o cursor está numa coluna (alias.coluna).</summary>
        public DbColumn Column { get; set; }
        /// <summary>Trecho do nome sob o cursor (para o tooltip).</summary>
        public int Start { get; set; }
        public int Length { get; set; }
    }

    /// <summary>Descobre o objeto/coluna sob o cursor: nome, schema.nome, alias, alias.coluna, tabela.coluna.</summary>
    public static class SymbolResolver
    {
        public static SymbolInfo At(string text, int pos, DatabaseMetadata md)
        {
            if (string.IsNullOrEmpty(text) || md == null) return null;
            var all = SqlTokenizer.Tokenize(text);
            // dentro de comentário ou string: nada
            if (all.Any(x => (x.Kind == TokenKind.Comment || x.Kind == TokenKind.String) && pos > x.Start && pos < x.End)) return null;
            var t = all.Where(x => x.Kind != TokenKind.Comment).ToList();
            int i = t.FindIndex(x => x.IsName && x.Start <= pos && pos <= x.End);
            if (i < 0) return null;
            if (i + 1 < t.Count && t[i + 1].IsName && t[i + 1].Start == pos) i++; // colado no próximo nome

            // cadeia a.b.c em volta
            int first = i, last = i;
            while (first >= 2 && t[first - 1].Kind == TokenKind.Dot && t[first - 2].IsName) first -= 2;
            while (last + 2 < t.Count && t[last + 1].Kind == TokenKind.Dot && t[last + 2].IsName) last += 2;
            var parts = new List<string>();
            for (int k = first; k <= i; k += 2) parts.Add(t[k].Value);
            var hovered = t[i];
            var result = new SymbolInfo { Start = hovered.Start, Length = hovered.Length };

            var ctx = SqlContextAnalyzer.Analyze(text, hovered.Start);
            var scope = CompletionEngine.Resolve(ctx, md);
            ResolvedTable Table(string refName) =>
                scope.FirstOrDefault(s => Eq(s.RefName, refName)) ?? scope.FirstOrDefault(s => Eq(s.Ref.Name, refName));

            // alias.coluna / tabela.coluna (último pedaço é coluna de uma tabela do escopo)
            if (parts.Count >= 2 && i == last)
            {
                var tbl = Table(parts[parts.Count - 2]);
                var col = tbl?.Object?.Columns.FirstOrDefault(c => Eq(c.Name, hovered.Value));
                if (col != null) { result.Object = tbl.Object; result.Column = col; return result; }
            }

            // alias sozinho (ou o qualificador de alias.coluna)
            if (parts.Count == 1)
            {
                var tbl = scope.FirstOrDefault(s => Eq(s.Ref.Alias, hovered.Value));
                if (tbl?.Object != null) { result.Object = tbl.Object; return result; }
            }

            var schema = parts.Count >= 2 ? parts[parts.Count - 2] : null;
            var obj = md.Find(schema, hovered.Value);
            if (obj == null && parts.Count == 1)
            {
                // coluna sem qualificador, de uma única tabela do escopo
                var owners = scope.Where(s => s.Object != null && s.Object.Columns.Any(c => Eq(c.Name, hovered.Value))).ToList();
                if (owners.Count == 1)
                {
                    result.Object = owners[0].Object;
                    result.Column = owners[0].Object.Columns.First(c => Eq(c.Name, hovered.Value));
                    return result;
                }
            }
            if (obj == null) return null;
            result.Object = obj;
            return result;
        }

        /// <summary>Texto curto do tooltip: tipo do objeto + colunas ou parâmetros.</summary>
        public static string Describe(SymbolInfo s, int maxLines = 30)
        {
            if (s?.Object == null) return null;
            var o = s.Object;
            if (s.Column != null)
            {
                var c = s.Column;
                var flags = new List<string>();
                if (c.IsPrimaryKey) flags.Add("PK");
                if (c.IsIdentity) flags.Add("identity");
                if (c.IsComputed) flags.Add("calculada");
                return o.FullName + "." + c.Name + "  " + c.DataType + (c.Nullable ? " NULL" : " NOT NULL") +
                       (flags.Count > 0 ? "  (" + string.Join(", ", flags) + ")" : "");
            }

            var lines = new List<string> { o.FullName + "  (" + KindName(o.Kind) + ")" };
            if (o.Parameters.Count > 0)
                lines.AddRange(o.Parameters.OrderBy(p => p.Ordinal).Select(p => "  " + p.Name + " " + p.DataType + (p.IsOutput ? " OUTPUT" : "")));
            var cols = o.Columns.OrderBy(c => c.Ordinal).ToList();
            foreach (var c in cols.Take(maxLines))
                lines.Add("  " + c.Name + " " + c.DataType + (c.IsPrimaryKey ? "  PK" : "") + (c.IsIdentity ? " identity" : "") + (c.Nullable ? "" : " NOT NULL"));
            if (cols.Count > maxLines) lines.Add("  ... e mais " + (cols.Count - maxLines) + " colunas");
            lines.Add("F12: ver a definição");
            return string.Join(Environment.NewLine, lines);
        }

        private static string KindName(DbObjectKind k)
        {
            switch (k)
            {
                case DbObjectKind.Table: return "tabela";
                case DbObjectKind.View: return "view";
                case DbObjectKind.Procedure: return "procedure";
                case DbObjectKind.ScalarFunction: return "função escalar";
                case DbObjectKind.TableFunction: return "função de tabela";
                case DbObjectKind.Synonym: return "sinônimo";
                default: return k.ToString().ToLowerInvariant();
            }
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
