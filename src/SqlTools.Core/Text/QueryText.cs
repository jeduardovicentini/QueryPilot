using System;
using System.Text.RegularExpressions;

namespace SqlTools.Text
{
    public static class QueryText
    {
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>Resumo de uma linha para listas: espaços colapsados, cortado em <paramref name="max"/> caracteres.</summary>
        public static string Summary(string sql, int max = 200)
        {
            if (string.IsNullOrEmpty(sql)) return string.Empty;
            var s = Whitespace.Replace(sql.Length > max * 4 ? sql.Substring(0, max * 4) : sql, " ").Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        /// <summary>Formata duração como hh:mm:ss (ou "0,123 s" abaixo de 1 segundo).</summary>
        public static string Duration(long? ms)
        {
            if (!ms.HasValue) return "";
            if (ms.Value < 1000) return (ms.Value / 1000.0).ToString("0.000") + " s";
            var t = TimeSpan.FromMilliseconds(ms.Value);
            return ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }
    }
}
