using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using SqlTools.Infra;
using SqlTools.Settings;

namespace SqlTools.Ssms.Completion
{
    /// <summary>
    /// Liga/desliga o "Auto list members" do IntelliSense nativo de T-SQL (RadLangSvc). Só a lista automática:
    /// sublinhado de erros e Quick Info continuam.
    /// </summary>
    internal static class NativeIntelliSense
    {
        public static readonly Guid SqlLanguageService = new Guid("c4d96929-a9b0-42cc-b3e0-adac0435d7f2");

        public static bool? GetAutoList()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(Package.GetGlobalService(typeof(SVsTextManager)) is IVsTextManager4 tm)) return null;
            var lang = new[] { new LANGPREFERENCES3 { guidLang = SqlLanguageService } };
            if (tm.GetUserPreferences4(null, lang, null) != 0) return null;
            return lang[0].fAutoListMembers != 0;
        }

        public static bool SetAutoList(bool on)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(Package.GetGlobalService(typeof(SVsTextManager)) is IVsTextManager4 tm)) return false;
            var lang = new[] { new LANGPREFERENCES3 { guidLang = SqlLanguageService } };
            if (tm.GetUserPreferences4(null, lang, null) != 0) return false;
            lang[0].fAutoListMembers = on ? 1u : 0u;
            return tm.SetUserPreferences4(null, lang, null) == 0;
        }

        /// <summary>Aplica a configuração: autocomplete ligado → lista nativa desligada; desligado → valor original de volta.</summary>
        public static void Apply(SettingsStore store)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var s = store.Current;
                var current = GetAutoList();
                if (current == null) return;

                if (s.CompletionEnabled)
                {
                    if (s.NativeAutoListOriginal < 0)
                    {
                        s.NativeAutoListOriginal = current.Value ? 1 : 0;
                        store.Save(s);
                    }
                    if (current.Value && SetAutoList(false))
                        Log.Info("Lista automática do IntelliSense nativo desligada (o " + ProductInfo.Name + " assume o autocomplete).");
                }
                else if (s.NativeAutoListOriginal >= 0)
                {
                    var restore = s.NativeAutoListOriginal == 1;
                    if (current.Value != restore) SetAutoList(restore);
                    s.NativeAutoListOriginal = -1;
                    store.Save(s);
                    Log.Info("Lista automática do IntelliSense nativo restaurada.");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível ajustar o IntelliSense nativo.", ex);
            }
        }
    }
}
