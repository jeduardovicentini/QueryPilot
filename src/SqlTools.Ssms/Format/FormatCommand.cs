using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlTools.Format;
using SqlTools.Infra;
using SqlTools.Settings;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.Format
{
    /// <summary>Formata a seleção (linhas inteiras) ou, sem seleção, a janela de query inteira.</summary>
    internal static class FormatCommand
    {
        public static string Execute(AppSettings settings)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var view = EditorAccess.GetActiveWpfTextView();
            if (view == null) return "abra uma janela de query para formatar.";
            return Execute(view, settings.ToFormatOptions());
        }

        public static string Execute(IWpfTextView view, FormatOptions options)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snapshot = view.TextSnapshot;
            var sel = view.Selection;
            bool partial = !sel.IsEmpty;
            SnapshotSpan span;
            string baseIndent = null;
            if (partial)
            {
                var first = sel.Start.Position.GetContainingLine();
                var endPos = sel.End.Position;
                var last = endPos.GetContainingLine();
                if (endPos == last.Start && last.LineNumber > first.LineNumber) last = snapshot.GetLineFromLineNumber(last.LineNumber - 1);
                span = new SnapshotSpan(first.Start, last.End);
                var line = first.GetText();
                baseIndent = line.Substring(0, line.Length - line.TrimStart().Length);
            }
            else span = new SnapshotSpan(snapshot, 0, snapshot.Length);

            var original = span.GetText();
            if (original.Trim().Length == 0) return "nada para formatar.";
            var r = SqlFormatter.Format(original, options, baseIndent);
            if (!r.Success)
            {
                Log.Info("Formatação cancelada: " + r.Error);
                return "não formatado: " + r.Error + ".";
            }
            if (r.Text == original) return "o SQL já está formatado.";

            // cursor: mesma posição relativa ao texto (só espaços mudam)
            int caret = view.Caret.Position.BufferPosition.Position;
            bool caretInside = caret >= span.Start && caret <= span.End;
            int sig = caretInside ? CountSignificant(original, caret - span.Start) : 0;

            // troca só o trecho que mudou: o editor preserva marcadores e a rolagem fora dele
            int pre = 0, max = Math.Min(original.Length, r.Text.Length);
            while (pre < max && original[pre] == r.Text[pre]) pre++;
            int suf = 0;
            while (suf < max - pre && original[original.Length - 1 - suf] == r.Text[r.Text.Length - 1 - suf]) suf++;
            using (var edit = view.TextBuffer.CreateEdit())
            {
                edit.Replace(span.Start + pre, original.Length - pre - suf, r.Text.Substring(pre, r.Text.Length - pre - suf));
                edit.Apply();
            }

            var now = view.TextSnapshot;
            if (partial)
            {
                var newSpan = new SnapshotSpan(now, span.Start.Position, r.Text.Length);
                view.Selection.Select(newSpan, false);
                view.Caret.MoveTo(newSpan.End);
            }
            else if (caretInside)
            {
                var pos = span.Start.Position + PositionAfter(r.Text, sig);
                view.Caret.MoveTo(new SnapshotPoint(now, Math.Min(pos, now.Length)));
            }
            view.ViewScroller.EnsureSpanVisible(new SnapshotSpan(view.Caret.Position.BufferPosition, 0));
            return null;
        }

        private static int CountSignificant(string s, int upTo)
        {
            int n = 0;
            for (int i = 0; i < upTo && i < s.Length; i++) if (!char.IsWhiteSpace(s[i])) n++;
            return n;
        }

        private static int PositionAfter(string s, int significant)
        {
            if (significant == 0) return 0;
            int n = 0;
            for (int i = 0; i < s.Length; i++)
                if (!char.IsWhiteSpace(s[i]) && ++n == significant) return i + 1;
            return s.Length;
        }
    }
}
