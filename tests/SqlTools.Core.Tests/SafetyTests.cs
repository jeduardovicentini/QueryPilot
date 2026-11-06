using System.Linq;
using SqlTools.Safety;
using Xunit;

namespace SqlTools.Core.Tests
{
    public class SafetyTests
    {
        private static string[] F(string sql) => UnsafeStatements.Find(sql).Select(x => x.ToString()).ToArray();

        [Fact]
        public void Acha_delete_e_update_sem_where()
        {
            Assert.Equal(new[] { "DELETE em dbo.Customer (linha 1)", "UPDATE em [Order] (linha 2)" },
                F("delete from dbo.Customer\nupdate [Order] set Total = 0\nselect 1"));
        }

        [Fact]
        public void Com_where_nao_alerta()
        {
            Assert.Empty(F("DELETE FROM t WHERE id = 1; UPDATE t SET a = 1 WHERE b = 2"));
            Assert.Empty(F("UPDATE t SET a = 1 FROM t LEFT JOIN u ON u.id = t.id WHERE u.id IS NULL"));
            Assert.Empty(F("DELETE FROM t WHERE CURRENT OF c"));
        }

        [Fact]
        public void Where_so_na_subconsulta_ainda_alerta()
        {
            Assert.Single(F("UPDATE t SET a = (SELECT MAX(x) FROM u WHERE u.id = 1)"));
        }

        [Fact]
        public void Ignora_temporarias_variaveis_comentarios_e_strings()
        {
            Assert.Empty(F("DELETE FROM #t; DELETE @t; UPDATE ##g SET a = 1; TRUNCATE TABLE #x"));
            Assert.Empty(F("-- DELETE FROM t\n/* UPDATE t SET a = 1 */ SELECT 'DELETE FROM t'"));
            Assert.Empty(F("DELETE x FROM #t x"));
        }

        [Fact]
        public void Inner_join_restringe_e_nao_alerta()
        {
            Assert.Empty(F("UPDATE c SET c.a = 1 FROM dbo.C c INNER JOIN dbo.D d ON d.id = c.id"));
            Assert.Empty(F("DELETE c FROM dbo.C c JOIN dbo.D d ON d.id = c.id"));
            Assert.Single(F("DELETE c FROM dbo.C c LEFT JOIN dbo.D d ON d.id = c.id"));
        }

        [Fact]
        public void Top_cte_truncate_e_blocos()
        {
            Assert.Equal(new[] { "DELETE em t (linha 1)" }, F("DELETE TOP (10) FROM t"));
            Assert.Equal(new[] { "DELETE em c (linha 1)" }, F("WITH c AS (SELECT * FROM t) DELETE FROM c"));
            Assert.Equal(new[] { "TRUNCATE em dbo.Log (linha 1)" }, F("TRUNCATE TABLE dbo.Log"));
            Assert.Equal(new[] { "UPDATE em t (linha 3)" }, F("IF @x = 1\nBEGIN\n  UPDATE t SET a = 1\nEND"));
        }

        [Fact]
        public void Nao_confunde_merge_gatilho_e_fk()
        {
            Assert.Empty(F("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN UPDATE SET a = s.a WHEN NOT MATCHED BY SOURCE THEN DELETE;"));
            Assert.Empty(F("CREATE TRIGGER tr ON dbo.T AFTER INSERT, UPDATE, DELETE AS IF UPDATE(a) PRINT 1"));
            Assert.Empty(F("ALTER TABLE t ADD CONSTRAINT fk FOREIGN KEY (a) REFERENCES u (id) ON DELETE CASCADE ON UPDATE NO ACTION"));
        }
    }
}
