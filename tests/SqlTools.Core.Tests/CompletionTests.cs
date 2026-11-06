using System.Collections.Generic;
using System.Data;
using System.Linq;
using SqlTools.Completion;
using SqlTools.Metadata;
using SqlTools.Sql;
using Xunit;

namespace SqlTools.Core.Tests
{
    public class TokenizerTests
    {
        [Fact]
        public void Tokeniza_colchetes_strings_comentarios_e_variaveis()
        {
            var t = SqlTokenizer.Tokenize("SELECT [Order Id], N'it''s' -- fim\n FROM dbo.[x]]y] /* a /* b */ c */ WHERE @id=1");
            Assert.Equal("Order Id", t[1].Value);
            Assert.Equal(TokenKind.String, t[3].Kind);
            Assert.Equal("N'it''s'", t[3].Text);
            Assert.Equal(TokenKind.Comment, t[4].Kind);
            Assert.Equal("x]y", t.First(x => x.Text == "[x]]y]").Value);
            Assert.Contains(t, x => x.Kind == TokenKind.Comment && x.Text.EndsWith("c */") && !x.Unterminated);
            Assert.Contains(t, x => x.Kind == TokenKind.Variable && x.Text == "@id");
        }

        [Fact]
        public void Marca_string_e_colchete_sem_fechamento()
        {
            var t = SqlTokenizer.Tokenize("SELECT 'abc");
            Assert.True(t.Last().Unterminated);
            t = SqlTokenizer.Tokenize("SELECT [Cust");
            Assert.True(t.Last().Unterminated);
            Assert.Equal("Cust", t.Last().Value);
        }
    }

    public class CompletionTests
    {
        internal static DatabaseMetadata Sample()
        {
            var md = new DatabaseMetadata { Server = "srv", Database = "Loja" };
            md.Schemas.AddRange(new[] { "dbo", "sales", "sys" });
            md.Databases.AddRange(new[] { "master", "Loja" });

            DbObject Obj(int id, string schema, string name, DbObjectKind kind, params DbColumn[] cols)
            {
                var o = new DbObject { ObjectId = id, Schema = schema, Name = name, Kind = kind };
                o.Columns.AddRange(cols);
                md.Objects.Add(o);
                return o;
            }
            DbColumn C(int ord, string name, string type, bool pk = false, bool identity = false) =>
                new DbColumn { Ordinal = ord, Name = name, DataType = type, IsPrimaryKey = pk, IsIdentity = identity };

            var customer = Obj(1, "dbo", "Customer", DbObjectKind.Table, C(1, "CustomerID", "int", true, true), C(2, "Name", "nvarchar(100)"), C(3, "Email", "varchar(200)"));
            var order = Obj(2, "dbo", "Order", DbObjectKind.Table, C(1, "OrderID", "int", true, true), C(2, "CustomerID", "int"), C(3, "OrderDate", "datetime2(7)"), C(4, "Total", "decimal(18,2)"));
            var item = Obj(3, "dbo", "OrderItem", DbObjectKind.Table, C(1, "OrderItemID", "int", true, true), C(2, "OrderID", "int"), C(3, "ProductID", "int"), C(4, "Qty", "int"));
            var product = Obj(4, "sales", "Product", DbObjectKind.Table, C(1, "ProductID", "int", true), C(2, "Name", "nvarchar(100)"), C(3, "Price", "money"));
            Obj(5, "sys", "objects", DbObjectKind.View, C(1, "name", "sysname"), C(2, "object_id", "int"));
            var proc = Obj(6, "dbo", "usp_GetCustomer", DbObjectKind.Procedure);
            proc.Parameters.Add(new DbParameter { Name = "@id", DataType = "int", Ordinal = 1 });
            Obj(7, "sys", "sp_who2", DbObjectKind.Procedure);

            ForeignKey Fk(string name, DbObject from, DbObject to, string fc, string tc)
            {
                var fk = new ForeignKey { Name = name, From = from, To = to };
                fk.Columns.Add(new KeyValuePair<string, string>(fc, tc));
                md.ForeignKeys.Add(fk);
                return fk;
            }
            Fk("FK_Order_Customer", order, customer, "CustomerID", "CustomerID");
            Fk("FK_Item_Order", item, order, "OrderID", "OrderID");
            Fk("FK_Item_Product", item, product, "ProductID", "ProductID");
            // objetos de sistema chegam separados (uma vez por servidor)
            var sys = md.Objects.Where(o => o.Schema == "sys").ToList();
            md.Objects.RemoveAll(o => o.Schema == "sys");
            md.SystemObjects = sys;
            md.BuildIndex();
            return md;
        }

        private static (SqlCompletionContext ctx, List<Suggestion> items) Run(string textWithCaret, CompletionOptions opt = null)
        {
            var caret = textWithCaret.IndexOf('|');
            var text = textWithCaret.Remove(caret, 1);
            var ctx = SqlContextAnalyzer.Analyze(text, caret);
            return (ctx, CompletionEngine.GetSuggestions(ctx, Sample(), opt));
        }

        [Theory]
        [InlineData("SELECT * FROM Customer WHERE Name = 'ab|c'")]
        [InlineData("SELECT 1 -- coment|")]
        [InlineData("SELECT 1 /* aberto |")]
        [InlineData("CREATE TABLE |")]
        [InlineData("SELECT Name AS |")]
        [InlineData("DECLARE |")]
        public void Sem_sugestoes_onde_nao_cabe(string sql)
        {
            Assert.Equal(ContextKind.None, Run(sql).ctx.Kind);
        }

        [Fact]
        public void Inicio_de_instrucao_so_palavras_chave()
        {
            var r = Run("SEL|");
            Assert.Equal(ContextKind.StatementStart, r.ctx.Kind);
            Assert.Equal("SEL", r.ctx.Prefix);
            Assert.All(r.items, i => Assert.Equal(SuggestionKind.Keyword, i.Kind));
        }

        [Fact]
        public void Lista_do_select_ve_colunas_do_from_que_vem_depois()
        {
            var r = Run("SELECT | FROM Customer c");
            Assert.Equal(ContextKind.Expression, r.ctx.Kind);
            Assert.Single(r.ctx.Tables);
            Assert.Equal("c", r.ctx.Tables[0].Alias);
            Assert.Contains(r.items, i => i.Kind == SuggestionKind.Column && i.DisplayText == "Email");
            Assert.Contains(r.items, i => i.Kind == SuggestionKind.Alias && i.DisplayText == "c");
        }

        [Fact]
        public void Coluna_ambigua_vem_com_alias()
        {
            var r = Run("SELECT | FROM Customer c JOIN [Order] o ON o.CustomerID = c.CustomerID");
            Assert.Contains(r.items, i => i.DisplayText == "c.CustomerID" && i.InsertText == "c.CustomerID" && i.FilterText == "CustomerID");
            Assert.Contains(r.items, i => i.DisplayText == "o.CustomerID");
            Assert.Contains(r.items, i => i.DisplayText == "Total" && i.InsertText == "Total");
        }

        [Fact]
        public void Alias_ponto_lista_colunas()
        {
            var r = Run("SELECT * FROM Customer c WHERE c.Na|");
            Assert.Equal(ContextKind.Member, r.ctx.Kind);
            Assert.Equal("Na", r.ctx.Prefix);
            Assert.Equal(new[] { "CustomerID", "Name", "Email" }, r.items.Select(i => i.DisplayText));
        }

        [Fact]
        public void From_sugere_tabelas_com_alias_e_schema_quando_nao_e_dbo()
        {
            var r = Run("SELECT * FROM |");
            Assert.Equal(ContextKind.ObjectName, r.ctx.Kind);
            Assert.Contains(r.items, i => i.DisplayText == "Customer" && i.InsertText == "Customer c");
            Assert.Contains(r.items, i => i.DisplayText == "Order" && i.InsertText == "[Order] o");
            Assert.Contains(r.items, i => i.DisplayText == "sales.Product" && i.InsertText == "sales.Product p" && i.FilterText == "Product");
            Assert.DoesNotContain(r.items, i => i.DisplayText.Contains("objects"));
            Assert.Contains(r.items, i => i.Kind == SuggestionKind.Schema && i.DisplayText == "sales");
        }

        [Fact]
        public void Alias_automatico_pode_ser_desligado()
        {
            var r = Run("SELECT * FROM |", new CompletionOptions { AutoAlias = false });
            Assert.Contains(r.items, i => i.DisplayText == "Customer" && i.InsertText == "Customer");
        }

        [Fact]
        public void Update_e_insert_nao_ganham_alias()
        {
            Assert.Contains(Run("UPDATE |").items, i => i.DisplayText == "Customer" && i.InsertText == "Customer");
            Assert.Contains(Run("INSERT INTO |").items, i => i.DisplayText == "Customer" && i.InsertText == "Customer");
        }

        [Fact]
        public void Schema_ponto_no_from_lista_objetos_do_schema()
        {
            var r = Run("SELECT * FROM sys.|");
            Assert.Equal(ContextKind.Member, r.ctx.Kind);
            Assert.Equal(ContextKind.ObjectName, r.ctx.QualifierPosition);
            Assert.Contains(r.items, i => i.DisplayText == "objects" && i.InsertText == "objects o");
            Assert.DoesNotContain(r.items, i => i.DisplayText == "Customer");
        }

        [Fact]
        public void Objetos_de_sistema_resolvem_e_aparecem_no_schema_sys()
        {
            var md = Sample();
            Assert.Equal("objects", md.Find("sys", "objects").Name);
            var r = Run("SELECT o.| FROM sys.objects o");
            Assert.Contains(r.items, i => i.DisplayText == "object_id");
        }

        [Fact]
        public void Join_sugere_join_completo_pela_fk()
        {
            var r = Run("SELECT * FROM Customer c JOIN |");
            Assert.True(r.ctx.IsJoin);
            var joins = r.items.Where(i => i.Kind == SuggestionKind.Join).Select(i => i.InsertText).ToList();
            Assert.Contains("[Order] o ON o.CustomerID = c.CustomerID", joins);
            Assert.Equal(0, r.items.First().Priority);
        }

        [Fact]
        public void Join_a_partir_da_tabela_filha_e_de_outro_schema()
        {
            var r = Run("SELECT * FROM OrderItem oi\nINNER JOIN |");
            var joins = r.items.Where(i => i.Kind == SuggestionKind.Join).Select(i => i.InsertText).ToList();
            Assert.Contains("[Order] o ON o.OrderID = oi.OrderID", joins);
            Assert.Contains("sales.Product p ON p.ProductID = oi.ProductID", joins);
        }

        [Fact]
        public void On_sugere_condicao_pela_fk()
        {
            var r = Run("SELECT * FROM [Order] o JOIN Customer c ON |");
            Assert.Equal(ContextKind.JoinCondition, r.ctx.Kind);
            Assert.Equal("Customer", r.ctx.JoinTarget.Name);
            Assert.Contains(r.items, i => i.Kind == SuggestionKind.JoinCondition && i.InsertText == "c.CustomerID = o.CustomerID");
        }

        [Fact]
        public void Exec_sugere_procedures_e_sp_de_sistema()
        {
            var r = Run("EXEC |");
            Assert.Equal(ContextKind.Procedure, r.ctx.Kind);
            Assert.Contains(r.items, i => i.DisplayText == "usp_GetCustomer" && i.Description.Contains("@id int"));
            Assert.Contains(r.items, i => i.DisplayText == "sp_who2");
        }

        [Fact]
        public void Use_sugere_bancos()
        {
            var r = Run("USE |");
            Assert.Equal(ContextKind.Database, r.ctx.Kind);
            Assert.Contains(r.items, i => i.DisplayText == "Loja");
        }

        [Fact]
        public void Insert_sugere_lista_de_colunas_sem_identity()
        {
            var r = Run("INSERT INTO Customer |");
            Assert.Equal(ContextKind.AfterInsertTarget, r.ctx.Kind);
            Assert.Contains(r.items, i => i.Kind == SuggestionKind.ColumnList && i.InsertText == "(Name, Email)");

            r = Run("INSERT INTO Customer (Name, |");
            Assert.Equal(ContextKind.InsertColumns, r.ctx.Kind);
            Assert.Contains(r.items, i => i.DisplayText == "Email" && i.Priority == 10);
            Assert.Contains(r.items, i => i.DisplayText == "CustomerID" && i.Priority == 20);
        }

        [Fact]
        public void Expande_asterisco_com_varias_tabelas()
        {
            var r = Run("SELECT *| FROM Customer c JOIN [Order] o ON o.CustomerID = c.CustomerID");
            Assert.Equal(ContextKind.StarExpansion, r.ctx.Kind);
            var s = Assert.Single(r.items);
            Assert.StartsWith("c.CustomerID, c.Name, c.Email, o.OrderID", s.InsertText);
            Assert.Equal(7, s.ReplaceStart);
            Assert.Equal(1, s.ReplaceLength);
        }

        [Fact]
        public void Expande_alias_asterisco_e_tabela_unica_sem_alias()
        {
            var r = Run("SELECT o.*| FROM Customer c JOIN [Order] o ON o.CustomerID = c.CustomerID");
            Assert.Equal("o.OrderID, o.CustomerID, o.OrderDate, o.Total", Assert.Single(r.items).InsertText);
            Assert.Equal(3, r.items[0].ReplaceLength);

            r = Run("SELECT *| FROM Customer");
            Assert.Equal("CustomerID, Name, Email", Assert.Single(r.items).InsertText);
        }

        [Fact]
        public void Instrucoes_separadas_sem_ponto_e_virgula()
        {
            var r = Run("SELECT * FROM Customer c\nSELECT | FROM [Order] o");
            Assert.Single(r.ctx.Tables);
            Assert.Equal("Order", r.ctx.Tables[0].Name);
        }

        [Fact]
        public void Case_end_nao_quebra_a_instrucao()
        {
            var r = Run("SELECT CASE WHEN 1 = 1 THEN 'a' ELSE 'b' END, | FROM Customer");
            Assert.Equal(ContextKind.Expression, r.ctx.Kind);
            Assert.Single(r.ctx.Tables);
        }

        [Fact]
        public void Go_separa_batches()
        {
            var r = Run("SELECT * FROM Customer c\nGO\nSELECT c.|");
            Assert.Empty(r.ctx.Tables);
        }

        [Fact]
        public void Variaveis_declaradas_no_batch()
        {
            var r = Run("DECLARE @id int, @nome varchar(10);\nSELECT * FROM Customer WHERE CustomerID = @|");
            Assert.Equal(ContextKind.Variable, r.ctx.Kind);
            Assert.Contains(r.items, i => i.DisplayText == "@id");
            Assert.Contains(r.items, i => i.DisplayText == "@nome");
        }

        [Fact]
        public void Cte_e_subconsulta_expoem_colunas()
        {
            var r = Run("WITH x AS (SELECT CustomerID, Name AS Nome FROM Customer)\nSELECT x.| FROM x");
            Assert.Equal(new[] { "CustomerID", "Nome" }, r.items.Select(i => i.DisplayText));

            r = Run("SELECT d.| FROM (SELECT Total AS T, OrderID FROM [Order]) d");
            Assert.Equal(new[] { "T", "OrderID" }, r.items.Select(i => i.DisplayText));
        }

        [Fact]
        public void Update_com_alias_do_from()
        {
            var r = Run("UPDATE c SET | FROM Customer c");
            Assert.Equal(ContextKind.Expression, r.ctx.Kind);
            Assert.Single(r.ctx.Tables);
            Assert.Contains(r.items, i => i.DisplayText == "Email");
        }

        [Fact]
        public void Depois_da_tabela_vem_palavras_chave()
        {
            var r = Run("SELECT * FROM Customer c |");
            Assert.Equal(ContextKind.AfterTableRef, r.ctx.Kind);
            Assert.All(r.items, i => Assert.Equal(SuggestionKind.Keyword, i.Kind));
        }

        [Fact]
        public void From_com_virgula_e_with_nolock()
        {
            var r = Run("SELECT | FROM Customer c WITH (NOLOCK), [Order] o");
            Assert.Equal(2, r.ctx.Tables.Count);
            r = Run("SELECT * FROM Customer c, |");
            Assert.Equal(ContextKind.ObjectName, r.ctx.Kind);
        }

        [Theory]
        [InlineData("Customer", "c")]
        [InlineData("SalesOrderHeader", "soh")]
        [InlineData("tb_pedido_item", "tpi")]
        [InlineData("Order", "o")]
        [InlineData("IndexStats", "is2")]
        public void Alias_gerado(string table, string alias)
        {
            Assert.Equal(alias, AliasGenerator.For(table, new List<string>()));
        }

        [Fact]
        public void Alias_nao_repete()
        {
            Assert.Equal("c2", AliasGenerator.For("Customer", new List<string> { "c" }));
        }

        [Theory]
        [InlineData("Name", "Name")]
        [InlineData("Order", "[Order]")]
        [InlineData("Order Id", "[Order Id]")]
        [InlineData("a]b", "[a]]b]")]
        public void Quote(string name, string expected)
        {
            Assert.Equal(expected, CompletionEngine.Quote(name));
        }
    }

    public class MetadataLoaderTests
    {
        [Theory]
        [InlineData("nvarchar", 100, 0, 0, "nvarchar(50)")]
        [InlineData("varchar", -1, 0, 0, "varchar(max)")]
        [InlineData("decimal", 9, 18, 2, "decimal(18,2)")]
        [InlineData("int", 4, 10, 0, "int")]
        public void Formata_tipos(string type, int len, int prec, int scale, string expected)
        {
            Assert.Equal(expected, MetadataLoader.FormatType(type, len, prec, scale));
        }

        [Fact]
        public void Carrega_result_sets()
        {
            DataTable T(params string[] cols)
            {
                var t = new DataTable();
                foreach (var c in cols) t.Columns.Add(c.Split(':')[0], c.EndsWith(":i") ? typeof(int) : c.EndsWith(":b") ? typeof(bool) : c.EndsWith(":s") ? typeof(short) : c.EndsWith(":y") ? typeof(byte) : typeof(string));
                return t;
            }
            var def = T("s"); def.Rows.Add("dbo");
            var schemas = T("name"); schemas.Rows.Add("dbo");
            var dbs = T("name"); dbs.Rows.Add("Loja");
            var objs = T("id:i", "sch", "name", "type"); objs.Rows.Add(1, "dbo", "Customer", "U "); objs.Rows.Add(2, "dbo", "Order", "U "); objs.Rows.Add(3, "dbo", "usp_X", "P ");
            var cols = T("id:i", "name", "col:i", "type", "len:s", "prec:y", "scale:y", "nul:b", "ident:b", "comp:b");
            cols.Rows.Add(1, "CustomerID", 1, "int", (short)4, (byte)10, (byte)0, false, true, false);
            cols.Rows.Add(1, "Name", 2, "nvarchar", (short)200, (byte)0, (byte)0, true, false, false);
            cols.Rows.Add(2, "OrderID", 1, "int", (short)4, (byte)10, (byte)0, false, true, false);
            cols.Rows.Add(2, "CustomerID", 2, "int", (short)4, (byte)10, (byte)0, false, false, false);
            var pks = T("id:i", "name"); pks.Rows.Add(1, "CustomerID");
            var pars = T("id:i", "name", "pid:i", "type", "len:s", "prec:y", "scale:y", "out:b"); pars.Rows.Add(3, "@id", 1, "int", (short)4, (byte)10, (byte)0, false);
            var fks = T("name", "parent:i", "ref:i", "pc", "rc"); fks.Rows.Add("FK_O_C", 2, 1, "CustomerID", "CustomerID");

            var md = MetadataLoader.Load(new DataTableReader(new[] { def, schemas, dbs, objs, cols, pks, pars, fks }), "srv", "Loja");

            var sysObjs = T("id:i", "sch", "name", "type"); sysObjs.Rows.Add(-385, "sys", "objects", "V ");
            var sysCols = T("id:i", "name", "col:i", "type", "len:s", "prec:y", "scale:y", "nul:b", "ident:b", "comp:b");
            sysCols.Rows.Add(-385, "name", 1, "sysname", (short)256, (byte)0, (byte)0, false, false, false);
            var system = MetadataLoader.LoadSystem(new DataTableReader(new[] { sysObjs, sysCols }));
            Assert.Equal("sysname", Assert.Single(Assert.Single(system).Columns).DataType);
            md.SystemObjects = system;
            Assert.NotNull(md.Find("sys", "objects"));
            Assert.Null(md.Find("dbo", "objects"));
            Assert.Equal(3, md.Objects.Count);
            var c = md.Find(null, "customer");
            Assert.Equal("nvarchar(100)", c.Columns[1].DataType);
            Assert.True(c.Columns[0].IsPrimaryKey);
            Assert.True(c.Columns[0].IsIdentity);
            Assert.Equal("@id", md.Find("dbo", "usp_X").Parameters[0].Name);
            var fk = Assert.Single(md.ForeignKeys);
            Assert.Equal("Order", fk.From.Name);
            Assert.Equal("Customer", fk.To.Name);
        }
    }
}
