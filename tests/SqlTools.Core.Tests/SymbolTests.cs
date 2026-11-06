using SqlTools.Completion;
using SqlTools.Sql;
using Xunit;

namespace SqlTools.Core.Tests
{
    public class SymbolTests
    {
        private static SymbolInfo At(string textWithCaret)
        {
            var caret = textWithCaret.IndexOf('|');
            return SymbolResolver.At(textWithCaret.Remove(caret, 1), caret, CompletionTests.Sample());
        }

        [Theory]
        [InlineData("SELECT * FROM Cust|omer", "dbo.Customer", null)]
        [InlineData("SELECT * FROM |Customer", "dbo.Customer", null)]
        [InlineData("SELECT * FROM Customer|", "dbo.Customer", null)]
        [InlineData("SELECT * FROM sales.Prod|uct", "sales.Product", null)]
        [InlineData("SELECT c|.Name FROM Customer c", "dbo.Customer", null)]
        [InlineData("SELECT c.Na|me FROM Customer c", "dbo.Customer", "Name")]
        [InlineData("SELECT Customer.Em|ail FROM Customer", "dbo.Customer", "Email")]
        [InlineData("SELECT Tot|al FROM [Order] o", "dbo.Order", "Total")]
        [InlineData("EXEC usp_Get|Customer 1", "dbo.usp_GetCustomer", null)]
        [InlineData("SELECT * FROM sys.obj|ects", "sys.objects", null)]
        public void Resolve_objeto_e_coluna(string sql, string obj, string col)
        {
            var s = At(sql);
            Assert.NotNull(s);
            Assert.Equal(obj, s.Object.FullName);
            Assert.Equal(col, s.Column?.Name);
        }

        [Theory]
        [InlineData("SELECT 'Custo|mer' FROM t")]
        [InlineData("-- Custo|mer\nSELECT 1")]
        [InlineData("SELECT * FROM NaoExi|ste")]
        [InlineData("SELECT Na|me FROM Customer c JOIN sales.Product p ON 1 = 1")]
        public void Nada_em_string_comentario_desconhecido_ou_ambiguo(string sql)
        {
            Assert.Null(At(sql));
        }

        [Fact]
        public void Descricao_lista_colunas_e_parametros()
        {
            var d = SymbolResolver.Describe(At("SELECT * FROM Custo|mer"));
            Assert.Contains("dbo.Customer  (tabela)", d);
            Assert.Contains("CustomerID int  PK identity", d);
            Assert.Contains("@nome nvarchar(50) OUTPUT", SymbolResolver.Describe(At("EXEC usp_Get|Customer")));
        }

        [Theory]
        [InlineData("CREATE PROCEDURE dbo.p AS SELECT 1", "ALTER PROCEDURE dbo.p AS SELECT 1")]
        [InlineData("-- cabeçalho CREATE\n/* x */ create   or alter view v as select 1", "-- cabeçalho CREATE\n/* x */ ALTER view v as select 1")]
        [InlineData("SELECT 1", "SELECT 1")]
        public void Create_vira_alter(string def, string expected)
        {
            Assert.Equal(expected, SqlScripts.CreateToAlter(def));
        }
    }
}
