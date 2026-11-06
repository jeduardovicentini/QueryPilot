using System;
using System.Data;
using SqlTools.Metadata;
using Xunit;

namespace SqlTools.Core.Tests
{
    public class TableScripterTests
    {
        private static DataTable T(int cols, params object[][] rows)
        {
            var t = new DataTable();
            for (int i = 0; i < cols; i++) t.Columns.Add("c" + i, typeof(object));
            foreach (var r in rows) t.Rows.Add(r);
            return t;
        }

        private static readonly object N = DBNull.Value;

        [Fact]
        public void Monta_create_table_com_constraints_fks_e_indices()
        {
            var ds = new DataSet();
            ds.Tables.Add(T(16,
                new object[] { "Id", "int", 4, 10, 0, false, true, "1", "1", N, N, N, N, false, "sys", false },
                new object[] { "Nome", "nvarchar", 200, 0, 0, false, false, N, N, N, N, N, N, false, "sys", false },
                new object[] { "Data", "datetime2", 8, 27, 7, false, false, N, N, N, N, "DF_Pedido_Data", "(sysdatetime())", false, "sys", false },
                new object[] { "Total", "decimal", 9, 18, 2, true, false, N, N, N, N, N, N, false, "sys", false },
                new object[] { "Dobro", "decimal", 9, 18, 2, true, false, N, N, "([Total]*(2))", true, N, N, false, "sys", false },
                new object[] { "Cpf", "TipoCpf", 11, 0, 0, true, false, N, N, N, N, N, N, true, "dbo", false }));
            ds.Tables.Add(T(7,
                new object[] { 1, "PK_Pedido", "CLUSTERED", true, false, true, N },
                new object[] { 2, "IX_Pedido_Data", "NONCLUSTERED", false, false, false, "([Total]>(0))" }));
            ds.Tables.Add(T(4,
                new object[] { 1, "Id", false, false },
                new object[] { 2, "Data", true, false },
                new object[] { 2, "Total", false, true }));
            ds.Tables.Add(T(7, new object[] { 99, "FK_Pedido_Cliente", "dbo", "Cliente", "CASCADE", "NO_ACTION", false }));
            ds.Tables.Add(T(3, new object[] { 99, "ClienteId", "Id" }));
            ds.Tables.Add(T(3, new object[] { "CK_Total", "([Total]>=(0))", false }));

            var s = TableScripter.Build(ds.CreateDataReader(), "dbo", "Pedido", "\nGO\n");
            Assert.Equal(string.Join("\n",
                "CREATE TABLE [dbo].[Pedido] (",
                "    [Id] int IDENTITY(1,1) NOT NULL,",
                "    [Nome] nvarchar(100) NOT NULL,",
                "    [Data] datetime2(7) NOT NULL CONSTRAINT [DF_Pedido_Data] DEFAULT (sysdatetime()),",
                "    [Total] decimal(18,2) NULL,",
                "    [Dobro] AS ([Total]*(2)) PERSISTED,",
                "    [Cpf] [dbo].[TipoCpf] NULL,",
                "    CONSTRAINT [PK_Pedido] PRIMARY KEY CLUSTERED ([Id] ASC),",
                "    CONSTRAINT [CK_Total] CHECK ([Total]>=(0))",
                ");",
                "GO",
                "ALTER TABLE [dbo].[Pedido] ADD CONSTRAINT [FK_Pedido_Cliente] FOREIGN KEY ([ClienteId]) REFERENCES [dbo].[Cliente] ([Id]) ON DELETE CASCADE;",
                "GO",
                "CREATE NONCLUSTERED INDEX [IX_Pedido_Data] ON [dbo].[Pedido] ([Data] DESC) INCLUDE ([Total]) WHERE ([Total]>(0));",
                "GO",
                ""), s.Replace("\r\n", "\n"));
        }
    }
}
