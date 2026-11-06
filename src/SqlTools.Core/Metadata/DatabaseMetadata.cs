using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlTools.Metadata
{
    public enum DbObjectKind { Table, View, Procedure, ScalarFunction, TableFunction, Synonym }

    public sealed class DbColumn
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public bool Nullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
        /// <summary>rowversion/timestamp: não aceita valor no INSERT.</summary>
        public bool IsRowVersion { get; set; }
        public int Ordinal { get; set; }

        public bool Insertable => !IsIdentity && !IsComputed && !IsRowVersion;
    }

    public sealed class DbParameter
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public bool IsOutput { get; set; }
        public int Ordinal { get; set; }
    }

    public sealed class DbObject
    {
        public int ObjectId { get; set; }
        public string Schema { get; set; }
        public string Name { get; set; }
        public DbObjectKind Kind { get; set; }
        public List<DbColumn> Columns { get; } = new List<DbColumn>();
        public List<DbParameter> Parameters { get; } = new List<DbParameter>();

        public bool IsTableLike => Kind == DbObjectKind.Table || Kind == DbObjectKind.View || Kind == DbObjectKind.TableFunction || Kind == DbObjectKind.Synonym;
        public string FullName => Schema + "." + Name;
        public override string ToString() => FullName;
    }

    public sealed class ForeignKey
    {
        public string Name { get; set; }
        public DbObject From { get; set; }
        public DbObject To { get; set; }
        /// <summary>Pares (coluna em From, coluna em To), na ordem da FK.</summary>
        public List<KeyValuePair<string, string>> Columns { get; } = new List<KeyValuePair<string, string>>();
    }

    /// <summary>Metadados de um banco, carregados uma vez e consultados pelo autocomplete.</summary>
    public sealed class DatabaseMetadata
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string DefaultSchema { get; set; } = "dbo";
        public DateTime LoadedAtUtc { get; set; } = DateTime.UtcNow;

        public List<string> Schemas { get; } = new List<string>();
        public List<string> Databases { get; } = new List<string>();
        public List<DbObject> Objects { get; } = new List<DbObject>();
        public List<ForeignKey> ForeignKeys { get; } = new List<ForeignKey>();

        private volatile IReadOnlyList<DbObject> _systemObjects = new DbObject[0];
        private volatile ILookup<string, DbObject> _byName;
        private volatile ILookup<string, DbObject> _systemByName;

        /// <summary>Objetos de sistema (sys, INFORMATION_SCHEMA), compartilhados pelo servidor e anexados quando chegam.</summary>
        public IReadOnlyList<DbObject> SystemObjects
        {
            get => _systemObjects;
            set
            {
                var list = value ?? new DbObject[0];
                _systemByName = list.ToLookup(o => o.Name, StringComparer.OrdinalIgnoreCase);
                _systemObjects = list;
            }
        }

        /// <summary>Objetos do banco + de sistema.</summary>
        public IEnumerable<DbObject> AllObjects => Objects.Concat(_systemObjects);

        public void BuildIndex()
        {
            _byName = Objects.ToLookup(o => o.Name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolve um nome de objeto como o SQL Server faria: com schema, exato; sem schema, o schema padrão primeiro,
        /// depois dbo, depois um único objeto com o nome em qualquer schema.
        /// </summary>
        public DbObject Find(string schema, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_byName == null) BuildIndex();
            var candidates = _byName[name].ToList();
            var sys = _systemByName;
            if (sys != null && (candidates.Count == 0 || !string.IsNullOrEmpty(schema))) candidates.AddRange(sys[name]);
            if (candidates.Count == 0) return null;
            if (!string.IsNullOrEmpty(schema))
                return candidates.FirstOrDefault(o => string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase));
            return candidates.FirstOrDefault(o => string.Equals(o.Schema, DefaultSchema, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(o => string.Equals(o.Schema, "dbo", StringComparison.OrdinalIgnoreCase))
                ?? (candidates.Count == 1 ? candidates[0] : null);
        }

        public IEnumerable<ForeignKey> ForeignKeysOf(DbObject obj) =>
            ForeignKeys.Where(fk => fk.From == obj || fk.To == obj);
    }
}
