using System;
using System.Data.Common;
using System.Linq;
using System.Text;
using SqlTools.Metadata;
using SqlTools.Sql;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.Navigation
{
    /// <summary>
    /// Script de definição de um objeto. Tabelas: CREATE TABLE montado do catálogo numa consulta (o SMO levava ~30 s);
    /// procedures/views/funções/gatilhos: OBJECT_DEFINITION como ALTER (igual ao "Modify").
    /// Roda fora da thread de UI.
    /// </summary>
    internal static class DefinitionScripter
    {
        private const string Go = "\r\nGO\r\n";

        public static string Script(ConnectionSnapshot conn, DbObject obj)
        {
            using (var db = SsmsBridge.CreateDbConnection(conn.UIConnectionInfo, conn.Database))
            {
                if (db == null) throw new InvalidOperationException("sem conexão com " + conn.Server);
                db.Open();
                var header = "USE " + SqlScripts.Bracket(db.Database) + Go;
                switch (obj.Kind)
                {
                    case DbObjectKind.Table:
                        return header + TableScript(db, obj);
                    case DbObjectKind.Synonym:
                        return header + SynonymScript(db, obj);
                    default:
                        return header + ModuleScript(db, obj);
                }
            }
        }

        private static string Qualified(DbObject o) => SqlScripts.Bracket(o.Schema) + "." + SqlScripts.Bracket(o.Name);

        private static object Scalar(DbConnection db, string sql, DbObject o, out object[] row)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = sql;
                var p = cmd.CreateParameter();
                p.ParameterName = "@n";
                p.Value = Qualified(o);
                cmd.Parameters.Add(p);
                using (var r = cmd.ExecuteReader())
                {
                    row = null;
                    if (!r.Read()) return null;
                    row = new object[r.FieldCount];
                    r.GetValues(row);
                    return row[0] is DBNull ? null : row[0];
                }
            }
        }

        private static string ModuleScript(DbConnection db, DbObject o)
        {
            var def = Scalar(db, "SELECT OBJECT_DEFINITION(OBJECT_ID(@n)), OBJECTPROPERTY(OBJECT_ID(@n), 'ExecIsAnsiNullsOn'), OBJECTPROPERTY(OBJECT_ID(@n), 'ExecIsQuotedIdentOn')", o, out var row) as string;
            if (def == null)
                return "-- A definição de " + Qualified(o) + " não está disponível (objeto criptografado ou sem permissão VIEW DEFINITION)." + Go;
            var sb = new StringBuilder();
            sb.Append("SET ANSI_NULLS ").Append(IsOff(row[1]) ? "OFF" : "ON").Append(Go);
            sb.Append("SET QUOTED_IDENTIFIER ").Append(IsOff(row[2]) ? "OFF" : "ON").Append(Go);
            sb.Append(SqlScripts.CreateToAlter(def.Trim('\r', '\n'))).Append(Go);
            return sb.ToString();
        }

        private static bool IsOff(object v) => v != null && !(v is DBNull) && Convert.ToInt32(v) == 0;

        private static string SynonymScript(DbConnection db, DbObject o)
        {
            var target = Scalar(db, "SELECT base_object_name FROM sys.synonyms WHERE object_id = OBJECT_ID(@n)", o, out _) as string;
            return "CREATE SYNONYM " + Qualified(o) + " FOR " + target + Go;
        }

        private static string TableScript(DbConnection db, DbObject o)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = TableScripter.Query;
                var p = cmd.CreateParameter();
                p.ParameterName = "@n";
                p.Value = Qualified(o);
                cmd.Parameters.Add(p);
                using (var r = cmd.ExecuteReader()) return TableScripter.Build(r, o.Schema, o.Name);
            }
        }
    }
}
