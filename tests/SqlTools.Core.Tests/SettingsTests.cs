using System;
using System.IO;
using SqlTools.Settings;
using SqlTools.Text;
using Xunit;

namespace SqlTools.Core.Tests
{
    public class ColorRuleTests
    {
        [Theory]
        [InlineData("PROD-*", "prod-sql01", true)]
        [InlineData("PROD-*", "DEV-SQL", false)]
        [InlineData("*\\SQL2019", "srv\\sql2019", true)]
        [InlineData("(localdb)\\*", "(localdb)\\MSSQLLocalDB", true)]
        [InlineData("db?", "db1", true)]
        [InlineData("db?", "db12", false)]
        [InlineData("", "x", false)]
        public void Matches_wildcards(string pattern, string server, bool expected)
        {
            Assert.Equal(expected, new ColorRule { ServerPattern = pattern }.Matches(server));
        }

        [Fact]
        public void FindMatch_returns_first_matching_rule()
        {
            var rules = new[]
            {
                new ColorRule { ServerPattern = "PROD-SQL01", Color = "#000001" },
                new ColorRule { ServerPattern = "PROD-*", Color = "#000002" },
            };
            Assert.Equal("#000001", ColorRule.FindMatch(rules, "prod-sql01").Color);
            Assert.Equal("#000002", ColorRule.FindMatch(rules, "PROD-SQL02").Color);
            Assert.Null(ColorRule.FindMatch(rules, "DEV"));
        }

        [Fact]
        public void TryParseColor()
        {
            Assert.True(ColorRule.TryParseColor("#D32F2F", out var r, out var g, out var b));
            Assert.Equal(0xD3, r); Assert.Equal(0x2F, g); Assert.Equal(0x2F, b);
            Assert.False(ColorRule.TryParseColor("vermelho", out _, out _, out _));
        }
    }

    public sealed class SettingsStoreTests : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "sqltools-settings-" + Guid.NewGuid().ToString("N") + ".json");

        public SettingsStoreTests()
        {
            // logs dos testes não podem ir para a pasta real do usuário
            SqlTools.Infra.AppPaths.Root = Path.Combine(Path.GetTempPath(), "sqltools-tests-data");
        }

        public void Dispose() { try { File.Delete(_file); } catch { } }

        [Fact]
        public void Missing_file_gives_defaults()
        {
            var s = new SettingsStore(_file).Current;
            Assert.True(s.CaptureEnabled);
            Assert.Equal(90, s.RetentionDays);
            Assert.Empty(s.ColorRules);
        }

        [Fact]
        public void Save_and_reload_roundtrip()
        {
            var store = new SettingsStore(_file);
            var s = store.Current;
            s.RetentionDays = 30;
            s.ColorRules.Add(new ColorRule { ServerPattern = "PROD-*", Color = "#D32F2F", Label = "Produção" });
            store.Save(s);

            var reloaded = new SettingsStore(_file).Current;
            Assert.Equal(30, reloaded.RetentionDays);
            Assert.Single(reloaded.ColorRules);
            Assert.Equal("Produção", reloaded.ColorRules[0].Label);
        }

        [Fact]
        public void Partial_json_keeps_defaults_for_missing_fields()
        {
            File.WriteAllText(_file, "{\"RetentionDays\": 7}");
            var s = new SettingsStore(_file).Current;
            Assert.Equal(7, s.RetentionDays);
            Assert.True(s.CaptureEnabled);
            Assert.True(s.TabColoringEnabled);
        }

        [Fact]
        public void Json_with_utf8_bom_is_read()
        {
            File.WriteAllText(_file, "{\"RetentionDays\": 12}", new System.Text.UTF8Encoding(true));
            Assert.Equal(12, new SettingsStore(_file).Current.RetentionDays);
        }

        [Fact]
        public void Corrupt_json_gives_defaults()
        {
            File.WriteAllText(_file, "{ isso não é json");
            Assert.Equal(90, new SettingsStore(_file).Current.RetentionDays);
        }

        [Fact]
        public void Current_is_a_copy()
        {
            var store = new SettingsStore(_file);
            store.Current.ColorRules.Add(new ColorRule());
            Assert.Empty(store.Current.ColorRules);
        }
    }

    public class QueryTextTests
    {
        [Fact]
        public void Summary_collapses_whitespace_and_truncates()
        {
            Assert.Equal("SELECT a FROM t", QueryText.Summary("SELECT a\r\n\tFROM   t"));
            Assert.Equal(10, QueryText.Summary(new string('x', 50), 10).Length);
        }

        [Fact]
        public void Duration_format()
        {
            Assert.Equal("", QueryText.Duration(null));
            Assert.Equal("00:01:05", QueryText.Duration(65_000));
        }
    }
}
