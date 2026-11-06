using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SqlTools.Infra;
using SqlTools.Sql;

namespace SqlTools.Ssms.Completion
{
    /// <summary>snippets.sql na pasta de dados: relido quando o arquivo muda (salvou, já vale).</summary>
    internal static class UserSnippetStore
    {
        private static readonly object Gate = new object();
        private static DateTime _stamp = DateTime.MinValue;
        private static long _size = -1;
        private static IReadOnlyList<SqlSnippet> _current = new List<SqlSnippet>();

        public static string FilePath => Path.Combine(AppPaths.Root, "snippets.sql");

        /// <summary>Problemas da última leitura (atalho inválido, vazio, repetido).</summary>
        public static IReadOnlyList<string> LastErrors { get; private set; } = new List<string>();

        public static IReadOnlyList<SqlSnippet> Current
        {
            get
            {
                lock (Gate)
                {
                    try
                    {
                        var info = new FileInfo(FilePath);
                        if (!info.Exists)
                        {
                            if (_size != -1) { _current = new List<SqlSnippet>(); _size = -1; _stamp = DateTime.MinValue; }
                            return _current;
                        }
                        if (info.LastWriteTimeUtc == _stamp && info.Length == _size) return _current;
                        var errors = new List<string>();
                        _current = SqlSnippets.Parse(File.ReadAllText(FilePath, Encoding.UTF8), errors);
                        _stamp = info.LastWriteTimeUtc;
                        _size = info.Length;
                        LastErrors = errors;
                        Log.Info("Snippets do usuário: " + _current.Count + " carregado(s)" + (errors.Count > 0 ? "; " + string.Join(" ", errors) : "."));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Falha ao ler " + FilePath + ".", ex);
                    }
                    return _current;
                }
            }
        }

        /// <summary>Cria o arquivo com exemplos, se ainda não existir.</summary>
        public static string EnsureFile()
        {
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, SqlSnippets.Template, new UTF8Encoding(true));
            return FilePath;
        }
    }
}
