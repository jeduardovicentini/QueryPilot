using System;

namespace SqlTools.History
{
    public enum ExecutionStatus
    {
        /// <summary>Execução iniciada; o fim ainda não foi capturado.</summary>
        Running = 0,
        Success = 1,
        Error = 2,
        Cancelled = 3,
        /// <summary>Não é execução: texto de uma aba de query fechada (para reabrir).</summary>
        ClosedTab = 4,
    }

    public sealed class HistoryEntry
    {
        public long Id { get; set; }
        public DateTime ExecutedAtUtc { get; set; }
        public string Server { get; set; }
        public string Database { get; set; }
        public string Login { get; set; }
        public long? DurationMs { get; set; }
        public long? RowsAffected { get; set; }
        public ExecutionStatus Status { get; set; }
        public string QueryText { get; set; }

        public DateTime ExecutedAtLocal => ExecutedAtUtc.ToLocalTime();
    }
}
