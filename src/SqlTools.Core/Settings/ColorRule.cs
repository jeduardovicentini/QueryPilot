using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace SqlTools.Settings
{
    /// <summary>Regra "servidor que casa com o padrão → cor da aba". Padrão aceita * e ?.</summary>
    [DataContract]
    public sealed class ColorRule
    {
        [DataMember] public string ServerPattern { get; set; } = "";
        /// <summary>Cor em #RRGGBB.</summary>
        [DataMember] public string Color { get; set; } = "#D32F2F";
        [DataMember] public string Label { get; set; } = "";

        public ColorRule Clone() => (ColorRule)MemberwiseClone();

        public bool Matches(string server)
        {
            if (string.IsNullOrWhiteSpace(ServerPattern) || string.IsNullOrEmpty(server)) return false;
            var regex = "^" + Regex.Escape(ServerPattern.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(server.Trim(), regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static ColorRule FindMatch(IEnumerable<ColorRule> rules, string server)
        {
            if (rules == null) return null;
            foreach (var r in rules)
                if (r != null && r.Matches(server)) return r;
            return null;
        }

        /// <summary>Converte #RRGGBB em componentes. Retorna false se inválido.</summary>
        public static bool TryParseColor(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            var h = hex.Trim().TrimStart('#');
            if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
            r = (byte)((v >> 16) & 0xFF);
            g = (byte)((v >> 8) & 0xFF);
            b = (byte)(v & 0xFF);
            return true;
        }

        /// <summary>Cores prontas para o usuário escolher.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> Presets = new[]
        {
            new KeyValuePair<string, string>("Vermelho", "#D32F2F"),
            new KeyValuePair<string, string>("Laranja", "#F57C00"),
            new KeyValuePair<string, string>("Amarelo", "#FBC02D"),
            new KeyValuePair<string, string>("Verde", "#388E3C"),
            new KeyValuePair<string, string>("Azul", "#1976D2"),
            new KeyValuePair<string, string>("Roxo", "#7B1FA2"),
            new KeyValuePair<string, string>("Cinza", "#616161"),
        };
    }
}
