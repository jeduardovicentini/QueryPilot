using System;
using System.IO;

namespace SqlTools.Infra
{
    public static class AppPaths
    {
        private static string _root;

        /// <summary>Pasta de dados do usuário. Pode ser sobrescrita (testes).</summary>
        public static string Root
        {
            get
            {
                if (_root == null)
                {
                    var overrideDir = Environment.GetEnvironmentVariable("SQLTOOLS_DATA_DIR");
                    _root = !string.IsNullOrEmpty(overrideDir)
                        ? overrideDir
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.DataFolderName);
                }
                Directory.CreateDirectory(_root);
                return _root;
            }
            set { _root = value; }
        }

        /// <summary>
        /// Copia history.db e settings.json de uma pasta de dados antiga (nome anterior do produto) quando ainda
        /// não existem na atual. Devolve quantos arquivos foram copiados.
        /// </summary>
        public static int MigrateFrom(string legacyFolderName)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SQLTOOLS_DATA_DIR"))) return 0;
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), legacyFolderName);
            if (!Directory.Exists(legacy) || string.Equals(Path.GetFullPath(legacy).TrimEnd('\\'), Path.GetFullPath(Root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return 0;

            var copied = 0;
            foreach (var name in new[] { "history.db", "settings.json" })
            {
                var src = Path.Combine(legacy, name);
                var dst = Path.Combine(Root, name);
                if (!File.Exists(src) || File.Exists(dst)) continue;
                File.Copy(src, dst);
                copied++;
            }
            return copied;
        }

        public static string HistoryDb => Path.Combine(Root, "history.db");
        public static string SettingsFile => Path.Combine(Root, "settings.json");

        public static string LogsDir
        {
            get
            {
                var dir = Path.Combine(Root, "logs");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }
}
