using SqlTools.Completion;
using Xunit;

namespace SqlTools.Core.Tests
{
    public class StarExpandTests
    {
        /// <summary>Aplica a expansão com o cursor em '|' e devolve o texto final (null se não expandiu).</summary>
        private static string Expand(string textWithCaret)
        {
            var caret = textWithCaret.IndexOf('|');
            var text = textWithCaret.Remove(caret, 1);
            var edits = CompletionEngine.ExpandStar(text, caret, CompletionTests.Sample());
            if (edits == null) return null;
            foreach (var e in edits) text = text.Remove(e.Start, e.Length).Insert(e.Start, e.Text);
            return text;
        }

        [Fact]
        public void Cria_alias_e_qualifica_colunas()
        {
            Assert.Equal("SELECT c.CustomerID, c.Name, c.Email FROM Customer c", Expand("SELECT |* FROM Customer"));
            Assert.Equal("SELECT c.CustomerID, c.Name, c.Email FROM Customer c", Expand("SELECT *| FROM Customer"));
        }

        [Fact]
        public void Usa_alias_existente()
        {
            Assert.Equal("SELECT x.CustomerID, x.Name, x.Email FROM dbo.Customer AS x WHERE x.Name = 'a'",
                Expand("SELECT * |FROM dbo.Customer AS x WHERE x.Name = 'a'".Replace("* |", "|* ")));
        }

        [Fact]
        public void Varias_tabelas_e_alias_ponto_estrela()
        {
            Assert.Equal("SELECT c.CustomerID, c.Name, c.Email, o.OrderID, o.CustomerID, o.OrderDate, o.Total FROM Customer c JOIN [Order] o ON o.CustomerID = c.CustomerID",
                Expand("SELECT |* FROM Customer c JOIN [Order] ON [Order].CustomerID = c.CustomerID"));
            Assert.Equal("SELECT o.OrderID, o.CustomerID, o.OrderDate, o.Total, c.Name FROM Customer c JOIN [Order] o ON o.CustomerID = c.CustomerID",
                Expand("SELECT o.*|, c.Name FROM Customer c JOIN [Order] o ON o.CustomerID = c.CustomerID"));
        }

        [Fact]
        public void Troca_referencias_pelo_nome_so_na_mesma_instrucao()
        {
            Assert.Equal("SELECT c.CustomerID, c.Name, c.Email FROM dbo.Customer c WHERE c.Name = 'a' ORDER BY c.Email;\nSELECT Customer.Name FROM Customer",
                Expand("SELECT *| FROM dbo.Customer WHERE Customer.Name = 'a' ORDER BY dbo.Customer.Email;\nSELECT Customer.Name FROM Customer"));
        }

        [Fact]
        public void Fora_do_star_nao_faz_nada()
        {
            Assert.Null(Expand("SELECT * FROM Cust|omer"));
            Assert.Null(Expand("SELECT a |* b FROM Customer"));
            Assert.Null(Expand("SELECT |* FROM TabelaQueNaoExiste"));
        }
    }
}
