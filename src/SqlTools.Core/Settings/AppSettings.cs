using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SqlTools.Settings
{
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public bool CaptureEnabled { get; set; } = true;
        [DataMember] public int RetentionDays { get; set; } = 90;
        [DataMember] public bool TabColoringEnabled { get; set; } = true;
        [DataMember] public List<ColorRule> ColorRules { get; set; } = new List<ColorRule>();
        /// <summary>Autocomplete do produto (desliga a lista automática do IntelliSense nativo enquanto ativo).</summary>
        [DataMember] public bool CompletionEnabled { get; set; } = true;
        /// <summary>Ao escolher uma tabela no FROM/JOIN, inserir um alias curto.</summary>
        [DataMember] public bool CompletionAutoAlias { get; set; } = true;
        /// <summary>Pedir confirmação antes de executar DELETE/UPDATE sem WHERE ou TRUNCATE.</summary>
        [DataMember] public bool UnsafeGuardEnabled { get; set; } = true;
        /// <summary>Guardar o texto das abas de query fechadas para reabrir.</summary>
        [DataMember] public bool ClosedTabsEnabled { get; set; } = true;
        /// <summary>Formatação: 0 = MAIÚSCULAS, 1 = minúsculas, 2 = manter.</summary>
        [DataMember] public int FormatKeywordCase { get; set; }
        /// <summary>Formatação: espaços por nível (0 = tabulação).</summary>
        [DataMember] public int FormatIndent { get; set; } = 4;
        [DataMember] public bool FormatLeadingCommas { get; set; }
        /// <summary>Interno: o atalho do formatador já foi configurado uma vez.</summary>
        [DataMember] public bool FormatShortcutApplied { get; set; }
        /// <summary>Interno: o atalho de reabrir aba fechada já foi configurado uma vez.</summary>
        [DataMember] public bool ReopenShortcutApplied { get; set; }
        /// <summary>Interno: o F12 (ir para a definição) já foi configurado uma vez.</summary>
        [DataMember] public bool DefinitionShortcutApplied { get; set; }
        [DataMember] public bool TestModeShortcutApplied { get; set; }
        /// <summary>Interno: valor original do "Auto list members" nativo (-1 = não guardado).</summary>
        [DataMember] public int NativeAutoListOriginal { get; set; } = -1;
        /// <summary>Interno: o atalho do histórico já foi configurado uma vez.</summary>
        [DataMember] public bool HistoryShortcutApplied { get; set; }
        /// <summary>Interno: o menu de topo já foi liberado uma vez em Extensions › Customize Menu.</summary>
        [DataMember] public bool TopMenuAllowed { get; set; }

        public AppSettings Clone()
        {
            var c = (AppSettings)MemberwiseClone();
            c.ColorRules = new List<ColorRule>();
            foreach (var r in ColorRules ?? new List<ColorRule>()) c.ColorRules.Add(r.Clone());
            return c;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext _)
        {
            if (ColorRules == null) ColorRules = new List<ColorRule>();
            if (RetentionDays < 0) RetentionDays = 0;
        }

        // Valores padrão para campos ausentes no JSON (DataContractSerializer não roda inicializadores).
        [OnDeserializing]
        private void OnDeserializing(StreamingContext _)
        {
            CaptureEnabled = true;
            RetentionDays = 90;
            TabColoringEnabled = true;
            ColorRules = new List<ColorRule>();
            CompletionEnabled = true;
            CompletionAutoAlias = true;
            NativeAutoListOriginal = -1;
            FormatIndent = 4;
            UnsafeGuardEnabled = true;
            ClosedTabsEnabled = true;
        }

        public Format.FormatOptions ToFormatOptions() => new Format.FormatOptions
        {
            KeywordCase = FormatKeywordCase == 1 ? Format.KeywordCase.Lower : FormatKeywordCase == 2 ? Format.KeywordCase.Keep : Format.KeywordCase.Upper,
            UseTabs = FormatIndent <= 0,
            IndentSize = FormatIndent <= 0 ? 4 : FormatIndent,
            LeadingCommas = FormatLeadingCommas
        };
    }
}
