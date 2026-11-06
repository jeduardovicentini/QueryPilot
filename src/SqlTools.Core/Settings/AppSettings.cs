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
        }
    }
}
