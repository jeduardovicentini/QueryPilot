using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using SqlTools.Infra;

namespace SqlTools.Settings
{
    public sealed class SettingsStore
    {
        private readonly string _path;
        private AppSettings _current;

        public SettingsStore(string path)
        {
            _path = path;
            _current = Load();
        }

        public event EventHandler Changed;

        /// <summary>Cópia das configurações atuais (alterar a cópia não afeta nada até chamar Save).</summary>
        public AppSettings Current => _current.Clone();

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var json = Serialize(settings);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
            _current = settings.Clone();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private AppSettings Load()
        {
            try
            {
                if (!File.Exists(_path)) return new AppSettings();
                // ReadAllText remove BOM (arquivo editado à mão no Notepad/PowerShell costuma ter)
                var bytes = new UTF8Encoding(false).GetBytes(File.ReadAllText(_path));
                using (var ms = new MemoryStream(bytes))
                    return (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(ms) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Warn("Configurações inválidas em " + _path + "; usando padrão.", ex);
                return new AppSettings();
            }
        }

        public static string Serialize(AppSettings s)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(w, s);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}
