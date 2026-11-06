using System;
using System.Collections.Generic;

namespace SqlTools.History
{
    public sealed class HistoryQuery
    {
        /// <summary>Texto procurado (contém, sem diferenciar maiúsculas).</summary>
        public string Text { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        /// <summary>Servidores aceitos; null ou vazio = todos.</summary>
        public IList<string> Servers { get; set; }
        /// <summary>Banco exato; null = todos.</summary>
        public string Database { get; set; }
        public bool OnlyErrors { get; set; }
        /// <summary>true = só abas fechadas; false = só execuções.</summary>
        public bool ClosedTabs { get; set; }
        public int Limit { get; set; } = 2000;
    }
}
