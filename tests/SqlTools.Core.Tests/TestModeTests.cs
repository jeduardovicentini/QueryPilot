using SqlTools.Safety;
using Xunit;

namespace SqlTools.Core.Tests
{
    public class TestModeTests
    {
        [Fact]
        public void Begin_tran_na_mesma_linha_e_rollback_em_lote_proprio()
        {
            var w = TestMode.Wrap("UPDATE dbo.T SET A = 1", "\n");
            Assert.Equal("BEGIN TRAN; ", w.Prefix);
            Assert.Equal("\nGO\nIF @@TRANCOUNT > 0 ROLLBACK TRAN; PRINT N'" + TestMode.Message + "';", w.Suffix);
        }

        [Theory]
        [InlineData("CREATE PROCEDURE dbo.P AS SELECT 1")]
        [InlineData("-- cabeçalho\r\nALTER VIEW dbo.V AS SELECT 1")]
        public void Create_alter_no_inicio_separa_o_begin_tran_em_outro_lote(string sql)
        {
            Assert.Equal("BEGIN TRAN;\r\nGO\r\n", TestMode.Wrap(sql).Prefix);
        }

        [Theory]
        [InlineData("SELECT 'abc")]
        [InlineData("SELECT 1 /* aberto")]
        [InlineData("   ")]
        public void Recusa_texto_incompleto_ou_vazio(string sql)
        {
            Assert.Null(TestMode.Wrap(sql));
        }

        [Theory]
        [InlineData("BEGIN TRAN; UPDATE T SET A = 1; COMMIT", true)]
        [InlineData("ROLLBACK TRANSACTION", true)]
        [InlineData("BEGIN TRAN; UPDATE T SET A = 1", false)]
        [InlineData("SELECT 'COMMIT' -- ROLLBACK", false)]
        [InlineData("SELECT [Commit] FROM T", false)]
        public void Detecta_commit_e_rollback(string sql, bool expected)
        {
            Assert.Equal(expected, TestMode.HasTransactionControl(sql));
        }
    }
}
