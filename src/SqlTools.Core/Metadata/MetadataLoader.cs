using System;
using System.Collections.Generic;
using System.Data;

namespace SqlTools.Metadata
{
    /// <summary>Lê o catálogo de um banco num único batch com vários result sets.</summary>
    public static class MetadataLoader
    {
        /// <summary>Objetos do banco. Ordem: schema padrão, schemas, bancos, objetos, colunas, PKs, parâmetros, FKs.</summary>
        /// <remarks>
        /// Só objetos do usuário (sys.objects/sys.columns): é rápido e é o que muda de banco para banco. Os de sistema
        /// são iguais no servidor inteiro e vêm de <see cref="SystemQuery"/>, uma vez por servidor.
        /// Nenhuma instrução pode pedir memory grant: num servidor ocupado (ou no LocalDB com o IntelliSense nativo
        /// carregando) ela fica em RESOURCE_SEMAPHORE por dezenas de segundos.
        /// </remarks>
        public const string Query = @"
SET NOCOUNT ON;
SELECT SCHEMA_NAME();
SELECT name FROM sys.schemas WHERE schema_id < 16384 ORDER BY name;
SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name;
SELECT o.object_id, SCHEMA_NAME(o.schema_id), o.name, o.type
FROM sys.objects o
WHERE o.is_ms_shipped = 0 AND o.type IN ('U','V','P','PC','FN','FS','IF','TF','FT','SN');
SELECT c.object_id, c.name, c.column_id, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed
FROM sys.columns c
JOIN sys.objects o ON o.object_id = c.object_id
WHERE o.is_ms_shipped = 0 AND o.type IN ('U','V','IF','TF','FT')
OPTION (LOOP JOIN);
SELECT ic.object_id, c.name
FROM sys.indexes i
JOIN sys.objects o ON o.object_id = i.object_id
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.is_primary_key = 1 AND o.is_ms_shipped = 0;
SELECT p.object_id, p.name, p.parameter_id, TYPE_NAME(p.user_type_id), p.max_length, p.precision, p.scale, p.is_output
FROM sys.parameters p
JOIN sys.objects o ON o.object_id = p.object_id
WHERE p.parameter_id > 0 AND o.is_ms_shipped = 0
ORDER BY p.object_id, p.parameter_id;
SELECT fk.name, fk.parent_object_id, fk.referenced_object_id, pc.name, rc.name
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
ORDER BY fk.object_id, fkc.constraint_column_id;";

        /// <summary>Views, funções e procedures de sistema (sys, INFORMATION_SCHEMA). Ordem: objetos, colunas.</summary>
        public const string SystemQuery = @"
SET NOCOUNT ON;
SELECT o.object_id, SCHEMA_NAME(o.schema_id), o.name, o.type
FROM sys.system_objects o
WHERE o.type IN ('V','IF','TF','FN','P','X') AND o.schema_id IN (SCHEMA_ID('sys'), SCHEMA_ID('INFORMATION_SCHEMA'));
SELECT c.object_id, c.name, c.column_id, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed
FROM sys.system_columns c
JOIN sys.system_objects o ON o.object_id = c.object_id
WHERE o.type IN ('V','IF','TF') AND o.schema_id IN (SCHEMA_ID('sys'), SCHEMA_ID('INFORMATION_SCHEMA'))
OPTION (LOOP JOIN);";

        public static List<DbObject> LoadSystem(IDataReader r)
        {
            var byId = new Dictionary<int, DbObject>();
            var list = new List<DbObject>();
            ReadObjects(r, byId, list);
            if (!r.NextResult()) throw new InvalidOperationException("Result set de colunas de sistema faltando.");
            ReadColumns(r, byId);
            return list;
        }

        private static void ReadObjects(IDataReader r, Dictionary<int, DbObject> byId, List<DbObject> list)
        {
            while (r.Read())
            {
                var kind = MapKind(r.GetString(3));
                if (kind == null) continue;
                var o = new DbObject { ObjectId = r.GetInt32(0), Schema = r.GetString(1), Name = r.GetString(2), Kind = kind.Value };
                byId[o.ObjectId] = o;
                list.Add(o);
            }
        }

        private static void ReadColumns(IDataReader r, Dictionary<int, DbObject> byId)
        {
            while (r.Read())
            {
                if (!byId.TryGetValue(r.GetInt32(0), out var o)) continue;
                o.Columns.Add(new DbColumn
                {
                    Name = r.GetString(1),
                    Ordinal = r.GetInt32(2),
                    DataType = FormatType(r.GetString(3), Convert.ToInt32(r.GetValue(4)), Convert.ToInt32(r.GetValue(5)), Convert.ToInt32(r.GetValue(6))),
                    IsRowVersion = string.Equals(r.GetString(3), "timestamp", StringComparison.OrdinalIgnoreCase),
                    Nullable = GetBool(r, 7),
                    IsIdentity = GetBool(r, 8),
                    IsComputed = GetBool(r, 9)
                });
            }
        }

        public static DatabaseMetadata Load(IDataReader r, string server, string database, Action<string> trace = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            void Next(IDataReader rd)
            {
                trace?.Invoke(sw.ElapsedMilliseconds.ToString());
                if (!rd.NextResult()) throw new InvalidOperationException("Result set de metadados faltando.");
            }
            var md = new DatabaseMetadata { Server = server, Database = database, LoadedAtUtc = DateTime.UtcNow };
            var byId = new Dictionary<int, DbObject>();

            if (r.Read() && !r.IsDBNull(0)) md.DefaultSchema = r.GetString(0);
            Next(r);
            while (r.Read()) md.Schemas.Add(r.GetString(0));
            Next(r);
            while (r.Read()) md.Databases.Add(r.GetString(0));
            Next(r);
            ReadObjects(r, byId, md.Objects);
            Next(r);
            ReadColumns(r, byId);
            Next(r);
            while (r.Read())
            {
                if (!byId.TryGetValue(r.GetInt32(0), out var o)) continue;
                var col = o.Columns.Find(c => string.Equals(c.Name, r.GetString(1), StringComparison.OrdinalIgnoreCase));
                if (col != null) col.IsPrimaryKey = true;
            }
            Next(r);
            while (r.Read())
            {
                if (!byId.TryGetValue(r.GetInt32(0), out var o)) continue;
                o.Parameters.Add(new DbParameter
                {
                    Name = r.GetString(1),
                    Ordinal = r.GetInt32(2),
                    DataType = FormatType(r.GetString(3), Convert.ToInt32(r.GetValue(4)), Convert.ToInt32(r.GetValue(5)), Convert.ToInt32(r.GetValue(6))),
                    IsOutput = GetBool(r, 7)
                });
            }
            Next(r);
            ForeignKey current = null;
            while (r.Read())
            {
                var name = r.GetString(0);
                if (!byId.TryGetValue(r.GetInt32(1), out var from) || !byId.TryGetValue(r.GetInt32(2), out var to)) continue;
                if (current == null || current.Name != name || current.From != from)
                {
                    current = new ForeignKey { Name = name, From = from, To = to };
                    md.ForeignKeys.Add(current);
                }
                current.Columns.Add(new KeyValuePair<string, string>(r.GetString(3), r.GetString(4)));
            }

            trace?.Invoke(sw.ElapsedMilliseconds.ToString());
            md.BuildIndex();
            return md;
        }

        private static bool GetBool(IDataReader r, int i) => !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i));

        public static DbObjectKind? MapKind(string type)
        {
            switch ((type ?? "").Trim().ToUpperInvariant())
            {
                case "U": return DbObjectKind.Table;
                case "V": return DbObjectKind.View;
                case "P": case "PC": case "X": return DbObjectKind.Procedure;
                case "FN": case "FS": return DbObjectKind.ScalarFunction;
                case "IF": case "TF": case "FT": return DbObjectKind.TableFunction;
                case "SN": return DbObjectKind.Synonym;
                default: return null;
            }
        }

        /// <summary>Monta o tipo como aparece no CREATE TABLE: nvarchar(50), decimal(18,2), varchar(max)...</summary>
        public static string FormatType(string type, int maxLength, int precision, int scale)
        {
            var t = type.ToLowerInvariant();
            switch (t)
            {
                case "varchar": case "char": case "varbinary": case "binary":
                    return t + "(" + (maxLength == -1 ? "max" : maxLength.ToString()) + ")";
                case "nvarchar": case "nchar":
                    return t + "(" + (maxLength == -1 ? "max" : (maxLength / 2).ToString()) + ")";
                case "decimal": case "numeric":
                    return t + "(" + precision + "," + scale + ")";
                case "datetime2": case "time": case "datetimeoffset":
                    return t + "(" + scale + ")";
                default:
                    return t;
            }
        }
    }
}
