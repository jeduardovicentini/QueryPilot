using SqlTools.Format;
using Xunit;

namespace SqlTools.Core.Tests
{
    public class FormatterTests
    {
        private static string F(string sql, FormatOptions o = null)
        {
            var r = SqlFormatter.Format(sql, o);
            Assert.True(r.Success, r.Error);
            return r.Text.Replace("\r\n", "\n");
        }

        private static string L(params string[] lines) => string.Join("\n", lines);

        [Fact]
        public void Select_com_join_e_where()
        {
            Assert.Equal(L(
                "SELECT",
                "    c.Id,",
                "    c.Name",
                "FROM dbo.Customer c",
                "INNER JOIN dbo.[Order] o",
                "    ON o.CustomerId = c.Id",
                "    AND o.Total > 10",
                "LEFT OUTER JOIN sales.Product p",
                "    ON p.Id = o.ProductId",
                "WHERE c.Id = 1",
                "    AND c.Name LIKE 'a%'",
                "ORDER BY c.Name"),
                F("select c.Id, c.Name from dbo.Customer c inner join dbo.[Order] o on o.CustomerId = c.Id and o.Total > 10 " +
                  "left outer join sales.Product p on p.Id = o.ProductId where c.Id = 1 and c.Name like 'a%' order by c.Name"));
        }

        [Fact]
        public void Item_unico_fica_na_linha_do_select()
        {
            Assert.Equal(L("SELECT *", "FROM t"), F("select * from t"));
            Assert.Equal(L("SELECT DISTINCT TOP (10) a", "FROM t"), F("select distinct top (10) a from t"));
        }

        [Fact]
        public void Preserva_comentarios()
        {
            Assert.Equal(L(
                "-- cabeçalho",
                "SELECT",
                "    a, -- primeira",
                "    b /* x */",
                "FROM t -- fim"),
                F("-- cabeçalho\nselect a, -- primeira\n b /* x */ from t -- fim"));
        }

        [Fact]
        public void Comentario_de_linha_sozinho_no_meio()
        {
            Assert.Equal(L(
                "SELECT",
                "    a,",
                "    -- b removido",
                "    c",
                "FROM t"),
                F("select a,\n-- b removido\nc from t"));
        }

        [Fact]
        public void Subconsulta_curta_fica_inline()
        {
            Assert.Equal(L(
                "SELECT a",
                "FROM t",
                "WHERE EXISTS (SELECT 1 FROM u WHERE u.id = t.id)"),
                F("select a from t where exists (select 1 from u where u.id = t.id)"));
        }

        [Fact]
        public void Subconsulta_longa_quebra_e_recua()
        {
            Assert.Equal(L(
                "SELECT s.a",
                "FROM (",
                "    SELECT",
                "        x.a,",
                "        x.b,",
                "        x.coluna_c",
                "    FROM dbo.TabelaComNomeLongo x",
                "    WHERE x.flag = 1",
                ") s"),
                F("select s.a from (select x.a, x.b, x.coluna_c from dbo.TabelaComNomeLongo x where x.flag = 1) s"));
        }

        [Fact]
        public void If_else_com_begin_end()
        {
            Assert.Equal(L(
                "IF @x = 1",
                "BEGIN",
                "    SELECT 1;",
                "    PRINT 'a'",
                "END",
                "ELSE IF @x = 2",
                "    PRINT 'b'",
                "ELSE",
                "    PRINT 'c'",
                "PRINT 'fim'"),
                F("if @x = 1 begin select 1; print 'a' end else if @x = 2 print 'b' else print 'c' print 'fim'"));
        }

        [Fact]
        public void Try_catch_e_transacao()
        {
            Assert.Equal(L(
                "BEGIN TRY",
                "    BEGIN TRAN;",
                "    DELETE FROM t",
                "    WHERE id = 1;",
                "    COMMIT;",
                "END TRY",
                "BEGIN CATCH",
                "    ROLLBACK;",
                "    THROW;",
                "END CATCH"),
                F("begin try begin tran; delete from t where id = 1; commit; end try begin catch rollback; throw; end catch"));
        }

        [Fact]
        public void Case_longo_quebra_e_curto_fica_inline()
        {
            Assert.Equal(L(
                "SELECT",
                "    CASE",
                "        WHEN a = 1 THEN 'um'",
                "        WHEN a = 2 THEN 'dois'",
                "        ELSE 'outro valor qualquer'",
                "    END AS x,",
                "    CASE WHEN b > 0 THEN 1 ELSE 0 END AS y",
                "FROM t"),
                F("select case when a = 1 then 'um' when a = 2 then 'dois' else 'outro valor qualquer' end as x, " +
                  "case when b > 0 then 1 else 0 end as y from t"));
        }

        [Fact]
        public void Insert_values_e_insert_select()
        {
            Assert.Equal(L("INSERT INTO dbo.T (a, b)", "VALUES (1, 'x')"), F("insert into dbo.T(a,b) values(1,'x')"));
            Assert.Equal(L(
                "INSERT INTO dbo.T (a)",
                "SELECT x.a",
                "FROM x"),
                F("insert into dbo.T (a) select x.a from x"));
        }

        [Fact]
        public void Update_e_between()
        {
            Assert.Equal(L(
                "UPDATE t",
                "SET a = 1, b = 2",
                "WHERE id BETWEEN 1 AND 10",
                "    AND c IS NOT NULL"),
                F("update t set a = 1, b = 2 where id between 1 and 10 and c is not null"));
        }

        [Fact]
        public void Create_table_coluna_por_linha()
        {
            Assert.Equal(L(
                "CREATE TABLE #t (",
                "    id int NOT NULL PRIMARY KEY,",
                "    nome varchar(50) NULL",
                ")"),
                F("create table #t (id int not null primary key, nome varchar(50) null)"));
        }

        [Fact]
        public void Procedure_com_parametros()
        {
            Assert.Equal(L(
                "CREATE PROCEDURE dbo.p",
                "    @a int,",
                "    @b varchar(10) = NULL OUTPUT",
                "AS",
                "BEGIN",
                "    SET NOCOUNT ON;",
                "    SELECT @a",
                "END"),
                F("create procedure dbo.p @a int, @b varchar(10) = null output as begin set nocount on; select @a end"));
        }

        [Fact]
        public void Cte_union_e_go()
        {
            Assert.Equal(L(
                "WITH c AS (SELECT a FROM t)",
                "SELECT *",
                "FROM c",
                "UNION ALL",
                "SELECT a",
                "FROM u",
                "GO",
                "",
                "SELECT 2"),
                F("with c as (select a from t) select * from c union all select a from u\ngo\n\nselect 2"));
        }

        [Fact]
        public void Operadores_e_funcoes()
        {
            Assert.Equal(L(
                "SELECT",
                "    a + b,",
                "    -1,",
                "    COUNT(*),",
                "    ISNULL(a, 0),",
                "    CAST(b AS int)",
                "FROM t WITH (NOLOCK)",
                "WHERE x >= 2",
                "    AND y <> 3",
                "    AND z = -1"),
                F("select a+b, -1, count(*), isnull(a,0), cast(b as int) from t with(nolock) where x>=2 and y<>3 and z = - 1"));
        }

        [Fact]
        public void Opcoes_minusculas_virgula_no_inicio_e_tab()
        {
            var o = new FormatOptions { KeywordCase = KeywordCase.Lower, LeadingCommas = true, UseTabs = true };
            Assert.Equal(L("select", "\ta", "\t, b", "from t"), F("SELECT a, b FROM t", o));
        }

        [Fact]
        public void Nao_muda_caixa_de_identificadores()
        {
            Assert.Equal(L("SELECT", "    t.Offset,", "    Output,", "    [select]", "FROM dbo.Date t"),
                F("select t.Offset, Output, [select] from dbo.Date t"));
        }

        [Fact]
        public void Recusa_texto_incompleto()
        {
            var r = SqlFormatter.Format("select 'abc from t");
            Assert.False(r.Success);
            Assert.Contains("string", r.Error);
        }

        [Fact]
        public void Recuo_base_para_selecao()
        {
            var r = SqlFormatter.Format("select a, b from t", new FormatOptions(), "    ");
            Assert.Equal(L("    SELECT", "        a,", "        b", "    FROM t"), r.Text.Replace("\r\n", "\n"));
        }

        [Theory]
        [InlineData("select c.Id, c.Name from dbo.Customer c inner join dbo.[Order] o on o.CustomerId = c.Id where c.Id = 1")]
        [InlineData("if @x = 1 begin select 1; print 'a' end else print 'b'")]
        [InlineData("create procedure dbo.p @a int, @b int as begin set nocount on; select @a, @b end")]
        [InlineData("merge into dbo.T as tgt using dbo.S as src on tgt.Id = src.Id when matched then update set tgt.a = src.a when not matched then insert (a) values (src.a);")]
        [InlineData("declare @t table (id int, nome varchar(10)); insert into @t values (1, 'a'), (2, 'b'); select * from @t")]
        [InlineData("select a, -- c\n b from (select x.a, x.b, x.ccccccccccccccc, x.dddddddddddddddd from x) s where s.a in (1, 2, 3)")]
        [InlineData("declare c cursor fast_forward for select a from t; open c; fetch next from c into @a; while @@fetch_status = 0 begin print @a; fetch next from c into @a end close c; deallocate c")]
        [InlineData("select row_number() over (partition by a order by b desc) as rn, sum(case when x = 1 then 1 else 0 end) from t group by a, b having count(*) > 1 order by a offset 10 rows fetch next 5 rows only")]
        [InlineData("alter table dbo.T add constraint fk foreign key (a) references dbo.U (id) on delete cascade; drop table if exists #t")]
        public void Idempotente_e_preserva_tokens(string sql)
        {
            var once = F(sql);
            Assert.Equal(once, F(once));

            // SQL pela metade (como durante a digitação): nunca quebra nem altera tokens
            for (int cut = 1; cut < sql.Length; cut++)
            {
                var part = sql.Substring(0, cut);
                var r = SqlFormatter.Format(part);
                Assert.True(r.Success || r.Error.Contains("sem"), "corte " + cut + ": " + r.Error);
            }
        }
    }
}
