using SqlTools.Completion;
using Xunit;

namespace SqlTools.Core.Tests
{
    public class InsertExpandTests
    {
        private static TextEdit Edit(string textWithCaret, out string text)
        {
            var caret = textWithCaret.IndexOf('|');
            text = textWithCaret.Remove(caret, 1);
            return CompletionEngine.ExpandInsert(text, caret, CompletionTests.Sample());
        }

        private static string Expand(string textWithCaret)
        {
            var e = Edit(textWithCaret, out var text);
            return e == null ? null : text.Remove(e.Start, e.Length).Insert(e.Start, e.Text);
        }

        [Fact]
        public void Gera_insert_no_layout_do_ssms_sem_identity()
        {
            var expected = string.Join("\r\n",
                "INSERT INTO [dbo].[Order]",
                "           ([CustomerID]",
                "           ,[OrderDate]",
                "           ,[Total])",
                "     VALUES",
                "           (<CustomerID>",
                "           ,<OrderDate>",
                "           ,<Total>)");
            Assert.Equal(expected, Expand("INSERT INTO [Order]|"));
            Assert.Equal(expected, Expand("insert into dbo.[Order]  |"));
            Assert.Equal(expected, Expand("INSERT [Order]|"));
        }

        [Fact]
        public void Seleciona_o_primeiro_valor()
        {
            var e = Edit("INSERT INTO sales.Product|", out _);
            Assert.Equal("<ProductID>", e.Text.Substring(e.SelectOffset, e.SelectLength));
        }

        [Fact]
        public void Mantem_recuo_da_linha_e_o_resto_do_script()
        {
            var r = Expand("SELECT 1;\n    INSERT INTO Customer|\nSELECT 2");
            Assert.Equal("SELECT 1;\n    INSERT INTO [dbo].[Customer]\n               ([Name]\n               ,[Email])\n         VALUES\n               (<Name>\n               ,<Email>)\nSELECT 2", r);
        }

        [Fact]
        public void So_quando_e_o_fim_do_insert()
        {
            Assert.Null(Expand("INSERT INTO Customer (Name)|"));
            Assert.Null(Expand("INSERT INTO Cust|omer"));
            Assert.Null(Expand("INSERT INTO Customer| (Name) VALUES ('a')"));
            Assert.Null(Expand("INSERT INTO NaoExiste|"));
            Assert.Null(Expand("SELECT * FROM Customer|"));
            Assert.Null(Expand("MERGE t USING s ON 1 = 1 WHEN NOT MATCHED THEN INSERT Customer|"));
        }
    }
}
