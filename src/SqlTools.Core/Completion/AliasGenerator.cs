using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SqlTools.Sql;

namespace SqlTools.Completion
{
    /// <summary>Gera alias curtos: Customer → c, SalesOrderHeader → soh, tb_pedido_item → tpi.</summary>
    public static class AliasGenerator
    {
        public static string For(string tableName, ICollection<string> used)
        {
            var baseAlias = Initials(tableName);
            if (string.IsNullOrEmpty(baseAlias)) baseAlias = "t";
            var alias = baseAlias;
            for (int n = 2; IsTaken(alias, used); n++) alias = baseAlias + n;
            return alias;
        }

        private static bool IsTaken(string alias, ICollection<string> used) =>
            SqlKeywords.Reserved.Contains(alias) || (used != null && used.Any(u => string.Equals(u, alias, StringComparison.OrdinalIgnoreCase)));

        public static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.TrimStart('#', '@');
            var sb = new StringBuilder();
            bool newWord = true;
            for (int i = 0; i < name.Length && sb.Length < 4; i++)
            {
                var c = name[i];
                if (c == '_' || c == ' ' || c == '$' || char.IsDigit(c)) { newWord = true; continue; }
                if (!char.IsLetter(c)) continue;
                // começo de palavra: depois de separador, ou maiúscula depois de minúscula (PascalCase)
                bool camel = i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]);
                if (newWord || camel) sb.Append(char.ToLowerInvariant(c));
                newWord = false;
            }
            return sb.ToString();
        }
    }
}
