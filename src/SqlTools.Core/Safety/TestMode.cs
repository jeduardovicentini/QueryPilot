using System.Linq;
using SqlTools.Sql;

namespace SqlTools.Safety
{
    public sealed class TestModeWrap
    {
        /// <summary>Inserido antes do texto executado.</summary>
        public string Prefix { get; set; }
        /// <summary>Inserido depois do texto executado.</summary>
        public string Suffix { get; set; }
    }

    /// <summary>
    /// Modo de teste: executa dentro de uma transação e desfaz no fim. O BEGIN TRAN fica na mesma linha do
    /// texto (as linhas dos erros não mudam), exceto quando o primeiro lote cria/altera objeto (CREATE PROC
    /// precisa ser o primeiro do lote). O ROLLBACK vai num lote próprio, para rodar mesmo se o último falhar.
    /// </summary>
    public static class TestMode
    {
        public const string Message = "QueryPilot - modo de teste: alterações desfeitas (ROLLBACK).";

        /// <summary>Null quando o texto termina dentro de comentário ou string (o sufixo seria engolido).</summary>
        public static TestModeWrap Wrap(string sql, string newLine = "\r\n")
        {
            if (string.IsNullOrWhiteSpace(sql)) return null;
            var tokens = SqlTokenizer.Tokenize(sql);
            if (tokens.Any(t => t.Unterminated)) return null;
            var first = tokens.FirstOrDefault(t => t.Kind != TokenKind.Comment);
            var prefix = first != null && first.Is("CREATE", "ALTER")
                ? "BEGIN TRAN;" + newLine + "GO" + newLine
                : "BEGIN TRAN; ";
            var suffix = newLine + "GO" + newLine + "IF @@TRANCOUNT > 0 ROLLBACK TRAN; PRINT N'" + Message + "';";
            return new TestModeWrap { Prefix = prefix, Suffix = suffix };
        }

        /// <summary>COMMIT/ROLLBACK no texto: o modo de teste não garante desfazer (ROLLBACK zera a transação).</summary>
        public static bool HasTransactionControl(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return false;
            return SqlTokenizer.Tokenize(sql).Any(t => t.Kind == TokenKind.Identifier && t.Is("COMMIT", "ROLLBACK"));
        }
    }
}
