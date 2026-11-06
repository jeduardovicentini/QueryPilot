using System.Collections.Generic;
using System.Linq;
using SqlTools.Completion;
using SqlTools.Sql;
using Xunit;

namespace SqlTools.Core.Tests
{
    public class SnippetTests
    {
        [Fact]
        public void Le_atalho_descricao_texto_e_cursor()
        {
            var list = SqlSnippets.Parse(
                "-- explicação ignorada\r\n" +
                "--#sel5 Primeiras 5\r\n" +
                "SELECT TOP 5 *\r\n" +
                "FROM $CURSOR$ WITH (NOLOCK)   \r\n" +
                "\r\n" +
                "--#quem\r\n" +
                "\r\n" +
                "SELECT 1;\r\n");
            Assert.Equal(2, list.Count);
            Assert.Equal("sel5", list[0].Shortcut);
            Assert.Equal("Primeiras 5", list[0].Description);
            Assert.Equal("SELECT TOP 5 *\r\nFROM  WITH (NOLOCK)", list[0].Text);
            Assert.Equal("SELECT TOP 5 *\r\nFROM ".Length, list[0].CursorOffset);
            Assert.True(list[0].IsUser);
            Assert.Equal("quem", list[1].Shortcut);
            Assert.Equal("SELECT 1;", list[1].Text);
            Assert.Equal("SELECT 1;", list[1].Description);
            Assert.Null(list[1].CursorOffset);
        }

        [Fact]
        public void Aponta_erros_e_atalho_repetido_vale_o_ultimo()
        {
            var errors = new List<string>();
            var list = SqlSnippets.Parse("--#a-b\r\nSELECT 1\r\n--#vazio\r\n\r\n--#x\r\nSELECT 1\r\n--#X\r\nSELECT 2", errors);
            Assert.Single(list);
            Assert.Equal("SELECT 2", list[0].Text);
            Assert.Equal(3, errors.Count);
            Assert.Contains("Linha 1", errors[0]);
            Assert.Contains("vazio", errors[1]);
            Assert.Contains("já existe", errors[2]);
        }

        [Fact]
        public void Template_e_valido()
        {
            var errors = new List<string>();
            var list = SqlSnippets.Parse(SqlSnippets.Template, errors);
            Assert.Empty(errors);
            Assert.Equal(new[] { "sel5", "quem" }, list.Select(x => x.Shortcut));
        }

        [Fact]
        public void Do_usuario_substitui_embutido_e_aparece_primeiro()
        {
            var user = SqlSnippets.Parse("--#ssf\r\nSELECT TOP 10 * FROM");
            var ctx = SqlContextAnalyzer.Analyze("ss", 2);
            var items = CompletionEngine.GetSuggestions(ctx, null, new CompletionOptions { UserSnippets = user })
                .Where(s => s.Kind == SuggestionKind.Snippet).ToList();
            var ssf = items.Where(s => s.DisplayText == "ssf").ToList();
            Assert.Single(ssf);
            Assert.Equal("SELECT TOP 10 * FROM", ssf[0].InsertText);
            Assert.True(ssf[0].Priority < items.First(s => s.DisplayText == "st").Priority);
        }

        [Fact]
        public void Lista_tem_todos_e_cada_bloco_pode_ser_copiado()
        {
            var user = SqlSnippets.Parse("--#ssf Meu ssf\r\nSELECT TOP 10 * FROM $CURSOR$");
            var catalog = SqlSnippets.Catalog(user, @"C:\x\snippets.sql");
            var errors = new List<string>();
            var parsed = SqlSnippets.Parse(catalog, errors);
            Assert.Contains(@"C:\x\snippets.sql", catalog);
            Assert.Contains("(substituído pelo seu)", catalog);
            // o ssf aparece duas vezes (o seu e o embutido); os demais, uma
            Assert.Single(errors);
            foreach (var b in SqlSnippets.BuiltIn.Where(x => x.Shortcut != "ssf"))
            {
                var p = parsed.Single(x => x.Shortcut == b.Shortcut);
                Assert.Equal(b.Text, p.Text);
                Assert.Equal(b.CursorOffset, p.CursorOffset);
            }
            Assert.True(catalog.IndexOf("--#ssf Meu ssf") < catalog.IndexOf("--#st "));
            Assert.True(catalog.IndexOf("--#btc ") < catalog.IndexOf("--#ij "));
        }

        [Fact]
        public void Expand_indenta_as_linhas_seguintes_e_posiciona_o_cursor()
        {
            var sn = SqlSnippets.BuiltIn.First(s => s.Shortcut == "btc");
            var text = SqlSnippets.Expand(sn.Text, sn.CursorOffset, "    ", "\n", out var caret);
            Assert.Equal("BEGIN TRY\n        \n    END TRY\n    BEGIN CATCH\n        THROW;\n    END CATCH", text);
            Assert.Equal("BEGIN TRY\n        ".Length, caret);

            var plain = SqlSnippets.Expand("SELECT * FROM", null, "  ", "\r\n", out caret);
            Assert.Equal("SELECT * FROM", plain);
            Assert.Equal(plain.Length, caret);
        }
    }
}
