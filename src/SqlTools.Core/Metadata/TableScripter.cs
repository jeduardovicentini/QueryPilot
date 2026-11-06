using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using SqlTools.Sql;

namespace SqlTools.Metadata
{
    /// <summary>
    /// CREATE TABLE completo a partir do catálogo, numa consulta só (o SMO leva ~30 s por tabela):
    /// colunas (tipo, identity, calculada, default), PK/UNIQUE, CHECK, FKs e índices.
    /// </summary>
    public static class TableScripter
    {
        /// <summary>Consulta com parâmetro @n = '[schema].[tabela]'. Devolve 6 result sets lidos por <see cref="Build"/>.</summary>
        public const string Query = @"
DECLARE @id int = OBJECT_ID(@n);
SELECT c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity,
       CAST(ic.seed_value AS nvarchar(40)), CAST(ic.increment_value AS nvarchar(40)),
       cc.definition, cc.is_persisted, dc.name, dc.definition, t.is_user_defined, SCHEMA_NAME(t.schema_id), c.is_rowguidcol
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
WHERE c.object_id = @id
ORDER BY c.column_id;
SELECT i.index_id, i.name, i.type_desc, i.is_primary_key, i.is_unique_constraint, i.is_unique, i.filter_definition
FROM sys.indexes i
WHERE i.object_id = @id AND i.type > 0 AND i.is_hypothetical = 0
ORDER BY i.is_primary_key DESC, i.is_unique_constraint DESC, i.name;
SELECT ic.index_id, c.name, ic.is_descending_key, ic.is_included_column
FROM sys.index_columns ic
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE ic.object_id = @id
ORDER BY ic.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id;
SELECT fk.object_id, fk.name, SCHEMA_NAME(rt.schema_id), rt.name, fk.delete_referential_action_desc, fk.update_referential_action_desc, fk.is_disabled
FROM sys.foreign_keys fk
JOIN sys.objects rt ON rt.object_id = fk.referenced_object_id
WHERE fk.parent_object_id = @id
ORDER BY fk.name;
SELECT fkc.constraint_object_id, pc.name, rc.name
FROM sys.foreign_key_columns fkc
JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
WHERE fkc.parent_object_id = @id
ORDER BY fkc.constraint_object_id, fkc.constraint_column_id;
SELECT name, definition, is_disabled FROM sys.check_constraints WHERE parent_object_id = @id ORDER BY name;";

        private sealed class Index
        {
            public int Id;
            public string Name, Type, Filter;
            public bool IsPk, IsUniqueConstraint, IsUnique;
            public readonly List<string> Keys = new List<string>();
            public readonly List<string> Included = new List<string>();
        }

        public static string Build(IDataReader r, string schema, string name, string go = "\r\nGO\r\n")
        {
            var table = SqlScripts.Bracket(schema) + "." + SqlScripts.Bracket(name);
            var lines = new List<string>();
            var after = new StringBuilder();

            while (r.Read())
            {
                var col = SqlScripts.Bracket(Str(r, 0));
                var computed = Str(r, 9);
                if (computed != null)
                {
                    lines.Add(col + " AS " + computed + (Bool(r, 10) ? " PERSISTED" : ""));
                    continue;
                }
                var type = Bool(r, 13)
                    ? SqlScripts.Bracket(Str(r, 14)) + "." + SqlScripts.Bracket(Str(r, 1))
                    : MetadataLoader.FormatType(Str(r, 1), Int(r, 2), Int(r, 3), Int(r, 4));
                var sb = new StringBuilder(col).Append(' ').Append(type);
                if (Bool(r, 6)) sb.Append(" IDENTITY(").Append(Str(r, 7) ?? "1").Append(',').Append(Str(r, 8) ?? "1").Append(')');
                if (Bool(r, 15)) sb.Append(" ROWGUIDCOL");
                sb.Append(Bool(r, 5) ? " NULL" : " NOT NULL");
                if (Str(r, 12) != null) sb.Append(" CONSTRAINT ").Append(SqlScripts.Bracket(Str(r, 11))).Append(" DEFAULT ").Append(Str(r, 12));
                lines.Add(sb.ToString());
            }

            r.NextResult();
            var indexes = new List<Index>();
            while (r.Read())
                indexes.Add(new Index
                {
                    Id = Int(r, 0), Name = Str(r, 1), Type = Str(r, 2), IsPk = Bool(r, 3), IsUniqueConstraint = Bool(r, 4), IsUnique = Bool(r, 5), Filter = Str(r, 6)
                });
            r.NextResult();
            while (r.Read())
            {
                var ix = indexes.FirstOrDefault(x => x.Id == Int(r, 0));
                if (ix == null) continue;
                var c = SqlScripts.Bracket(Str(r, 1));
                if (Bool(r, 3)) ix.Included.Add(c);
                else ix.Keys.Add(c + (Bool(r, 2) ? " DESC" : " ASC"));
            }
            foreach (var ix in indexes.Where(x => x.IsPk || x.IsUniqueConstraint))
                lines.Add("CONSTRAINT " + SqlScripts.Bracket(ix.Name) + (ix.IsPk ? " PRIMARY KEY " : " UNIQUE ") + Kind(ix.Type) + " (" + string.Join(", ", ix.Keys) + ")");

            r.NextResult();
            var fks = new List<(int Id, string Name, string Ref, string OnDelete, string OnUpdate, bool Disabled, List<string> From, List<string> To)>();
            while (r.Read())
                fks.Add((Int(r, 0), Str(r, 1), SqlScripts.Bracket(Str(r, 2)) + "." + SqlScripts.Bracket(Str(r, 3)), Str(r, 4), Str(r, 5), Bool(r, 6), new List<string>(), new List<string>()));
            r.NextResult();
            while (r.Read())
            {
                var fk = fks.FirstOrDefault(x => x.Id == Int(r, 0));
                if (fk.Name == null) continue;
                fk.From.Add(SqlScripts.Bracket(Str(r, 1)));
                fk.To.Add(SqlScripts.Bracket(Str(r, 2)));
            }

            r.NextResult();
            var checks = new List<string>();
            while (r.Read()) checks.Add("CONSTRAINT " + SqlScripts.Bracket(Str(r, 0)) + " CHECK " + Str(r, 1));
            lines.AddRange(checks);

            var script = new StringBuilder("CREATE TABLE ").Append(table).Append(" (\r\n    ")
                .Append(string.Join(",\r\n    ", lines)).Append("\r\n);").Append(go);
            foreach (var fk in fks)
            {
                script.Append("ALTER TABLE ").Append(table).Append(" ADD CONSTRAINT ").Append(SqlScripts.Bracket(fk.Name))
                    .Append(" FOREIGN KEY (").Append(string.Join(", ", fk.From)).Append(") REFERENCES ").Append(fk.Ref)
                    .Append(" (").Append(string.Join(", ", fk.To)).Append(')');
                if (fk.OnDelete != null && fk.OnDelete != "NO_ACTION") script.Append(" ON DELETE ").Append(fk.OnDelete.Replace('_', ' '));
                if (fk.OnUpdate != null && fk.OnUpdate != "NO_ACTION") script.Append(" ON UPDATE ").Append(fk.OnUpdate.Replace('_', ' '));
                script.Append(';').Append(go);
                if (fk.Disabled) script.Append("ALTER TABLE ").Append(table).Append(" NOCHECK CONSTRAINT ").Append(SqlScripts.Bracket(fk.Name)).Append(';').Append(go);
            }
            foreach (var ix in indexes.Where(x => !x.IsPk && !x.IsUniqueConstraint))
            {
                if (ix.Type.Contains("COLUMNSTORE"))
                {
                    script.Append("CREATE ").Append(ix.Type.Replace('_', ' ')).Append(" INDEX ").Append(SqlScripts.Bracket(ix.Name)).Append(" ON ").Append(table);
                    if (ix.Type.StartsWith("NONCLUSTERED")) script.Append(" (").Append(string.Join(", ", ix.Included.Concat(ix.Keys.Select(k => k.Replace(" ASC", "").Replace(" DESC", ""))))).Append(')');
                    script.Append(';').Append(go);
                    continue;
                }
                script.Append("CREATE ").Append(ix.IsUnique ? "UNIQUE " : "").Append(Kind(ix.Type)).Append(" INDEX ").Append(SqlScripts.Bracket(ix.Name))
                    .Append(" ON ").Append(table).Append(" (").Append(string.Join(", ", ix.Keys)).Append(')');
                if (ix.Included.Count > 0) script.Append(" INCLUDE (").Append(string.Join(", ", ix.Included)).Append(')');
                if (ix.Filter != null) script.Append(" WHERE ").Append(ix.Filter);
                script.Append(';').Append(go);
            }
            return script.ToString();
        }

        private static string Kind(string typeDesc) => typeDesc != null && typeDesc.StartsWith("CLUSTERED") ? "CLUSTERED" : "NONCLUSTERED";
        private static string Str(IDataRecord r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
        private static int Int(IDataRecord r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
        private static bool Bool(IDataRecord r, int i) => !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i));
    }
}
