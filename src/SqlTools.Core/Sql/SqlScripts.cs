using System.Linq;

namespace SqlTools.Sql
{
    public static class SqlScripts
    {
        /// <summary>
        /// Troca o primeiro CREATE (ou CREATE OR ALTER) do código de um módulo por ALTER, como o "Modify" do SSMS.
        /// Comentários e strings antes dele ficam intactos.
        /// </summary>
        public static string CreateToAlter(string definition)
        {
            if (string.IsNullOrEmpty(definition)) return definition;
            var t = SqlTokenizer.Tokenize(definition).Where(x => x.Kind != TokenKind.Comment).ToList();
            if (t.Count == 0 || !t[0].Is("CREATE")) return definition;
            int end = t[0].End;
            if (t.Count >= 3 && t[1].Is("OR") && t[2].Is("ALTER")) end = t[2].End;
            return definition.Substring(0, t[0].Start) + "ALTER" + definition.Substring(end);
        }

        public static string Bracket(string name) => "[" + (name ?? "").Replace("]", "]]") + "]";
    }
}
