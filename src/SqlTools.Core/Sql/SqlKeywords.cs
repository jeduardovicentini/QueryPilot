using System;
using System.Collections.Generic;

namespace SqlTools.Sql
{
    public static class SqlKeywords
    {
        /// <summary>Palavras reservadas do T-SQL (não podem ser alias sem colchetes).</summary>
        public static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ADD","ALL","ALTER","AND","ANY","AS","ASC","AUTHORIZATION","BACKUP","BEGIN","BETWEEN","BREAK","BROWSE","BULK","BY",
            "CASCADE","CASE","CHECK","CHECKPOINT","CLOSE","CLUSTERED","COALESCE","COLLATE","COLUMN","COMMIT","COMPUTE","CONSTRAINT",
            "CONTAINS","CONTAINSTABLE","CONTINUE","CONVERT","CREATE","CROSS","CURRENT","CURRENT_DATE","CURRENT_TIME","CURRENT_TIMESTAMP",
            "CURRENT_USER","CURSOR","DATABASE","DBCC","DEALLOCATE","DECLARE","DEFAULT","DELETE","DENY","DESC","DISK","DISTINCT",
            "DISTRIBUTED","DOUBLE","DROP","DUMP","ELSE","END","ERRLVL","ESCAPE","EXCEPT","EXEC","EXECUTE","EXISTS","EXIT","EXTERNAL",
            "FETCH","FILE","FILLFACTOR","FOR","FOREIGN","FREETEXT","FREETEXTTABLE","FROM","FULL","FUNCTION","GOTO","GRANT","GROUP",
            "HAVING","HOLDLOCK","IDENTITY","IDENTITY_INSERT","IDENTITYCOL","IF","IN","INDEX","INNER","INSERT","INTERSECT","INTO","IS",
            "JOIN","KEY","KILL","LEFT","LIKE","LINENO","LOAD","MERGE","NATIONAL","NOCHECK","NONCLUSTERED","NOT","NULL","NULLIF","OF",
            "OFF","OFFSETS","ON","OPEN","OPENDATASOURCE","OPENQUERY","OPENROWSET","OPENXML","OPTION","OR","ORDER","OUTER","OVER",
            "PERCENT","PIVOT","PLAN","PRECISION","PRIMARY","PRINT","PROC","PROCEDURE","PUBLIC","RAISERROR","READ","READTEXT",
            "RECONFIGURE","REFERENCES","REPLICATION","RESTORE","RESTRICT","RETURN","REVERT","REVOKE","RIGHT","ROLLBACK","ROWCOUNT",
            "ROWGUIDCOL","RULE","SAVE","SCHEMA","SECURITYAUDIT","SELECT","SEMANTICKEYPHRASETABLE","SEMANTICSIMILARITYDETAILSTABLE",
            "SEMANTICSIMILARITYTABLE","SESSION_USER","SET","SETUSER","SHUTDOWN","SOME","STATISTICS","SYSTEM_USER","TABLE","TABLESAMPLE",
            "TEXTSIZE","THEN","TO","TOP","TRAN","TRANSACTION","TRIGGER","TRUNCATE","TRY_CONVERT","TSEQUAL","UNION","UNIQUE","UNPIVOT",
            "UPDATE","UPDATETEXT","USE","USER","VALUES","VARYING","VIEW","WAITFOR","WHEN","WHERE","WHILE","WITH","WITHIN","WRITETEXT",
            // não reservadas oficialmente, mas nunca são alias depois de uma tabela
            "APPLY","OUTPUT","OFFSET","USING","GO","THROW","WINDOW","MATCHED"
        };

        /// <summary>Palavras-chave sugeridas no autocomplete (ordem: mais usadas primeiro).</summary>
        public static readonly string[] Suggested =
        {
            "SELECT","FROM","WHERE","AND","OR","NOT","IN","EXISTS","LIKE","BETWEEN","IS NULL","IS NOT NULL","AS","ON",
            "JOIN","INNER JOIN","LEFT JOIN","RIGHT JOIN","FULL JOIN","CROSS JOIN","CROSS APPLY","OUTER APPLY",
            "GROUP BY","ORDER BY","HAVING","DISTINCT","TOP","UNION","UNION ALL","EXCEPT","INTERSECT",
            "INSERT INTO","VALUES","UPDATE","SET","DELETE","DELETE FROM","MERGE","OUTPUT","INTO",
            "CASE","WHEN","THEN","ELSE","END","NULL","ASC","DESC","WITH","OVER","PARTITION BY","ROWS","RANGE",
            "OFFSET","FETCH NEXT","ROWS ONLY","DECLARE","EXEC","EXECUTE","BEGIN","END","IF","WHILE","RETURN","PRINT",
            "BEGIN TRAN","COMMIT","ROLLBACK","BEGIN TRY","END TRY","BEGIN CATCH","END CATCH","THROW","RAISERROR",
            "CREATE TABLE","CREATE VIEW","CREATE PROCEDURE","CREATE FUNCTION","CREATE INDEX","ALTER TABLE","ALTER VIEW",
            "ALTER PROCEDURE","ALTER FUNCTION","DROP TABLE","DROP VIEW","DROP PROCEDURE","DROP FUNCTION","TRUNCATE TABLE",
            "PRIMARY KEY","FOREIGN KEY","REFERENCES","DEFAULT","CONSTRAINT","IDENTITY","NOLOCK","USE","GO","OPTION","RECOMPILE",
            "COLLATE","PIVOT","UNPIVOT","WAITFOR DELAY","SET NOCOUNT ON","SET XACT_ABORT ON"
        };

        /// <summary>Funções nativas mais comuns (nome, assinatura curta).</summary>
        public static readonly KeyValuePair<string, string>[] Functions =
        {
            F("COUNT", "COUNT(expressão | *)"), F("SUM", "SUM(expressão)"), F("AVG", "AVG(expressão)"), F("MIN", "MIN(expressão)"), F("MAX", "MAX(expressão)"),
            F("COUNT_BIG", "COUNT_BIG(expressão | *)"), F("STRING_AGG", "STRING_AGG(expressão, separador)"), F("STDEV", "STDEV(expressão)"), F("VAR", "VAR(expressão)"),
            F("ISNULL", "ISNULL(expressão, substituto)"), F("COALESCE", "COALESCE(expr1, expr2, ...)"), F("NULLIF", "NULLIF(expr1, expr2)"), F("IIF", "IIF(condição, verdadeiro, falso)"),
            F("CAST", "CAST(expressão AS tipo)"), F("CONVERT", "CONVERT(tipo, expressão [, estilo])"), F("TRY_CAST", "TRY_CAST(expressão AS tipo)"), F("TRY_CONVERT", "TRY_CONVERT(tipo, expressão [, estilo])"),
            F("PARSE", "PARSE(texto AS tipo [USING cultura])"), F("TRY_PARSE", "TRY_PARSE(texto AS tipo [USING cultura])"), F("FORMAT", "FORMAT(valor, formato [, cultura])"),
            F("GETDATE", "GETDATE()"), F("GETUTCDATE", "GETUTCDATE()"), F("SYSDATETIME", "SYSDATETIME()"), F("SYSUTCDATETIME", "SYSUTCDATETIME()"), F("SYSDATETIMEOFFSET", "SYSDATETIMEOFFSET()"),
            F("DATEADD", "DATEADD(parte, número, data)"), F("DATEDIFF", "DATEDIFF(parte, início, fim)"), F("DATEDIFF_BIG", "DATEDIFF_BIG(parte, início, fim)"), F("DATEPART", "DATEPART(parte, data)"),
            F("DATENAME", "DATENAME(parte, data)"), F("DATEFROMPARTS", "DATEFROMPARTS(ano, mês, dia)"), F("EOMONTH", "EOMONTH(data [, meses])"), F("DATETRUNC", "DATETRUNC(parte, data)"),
            F("YEAR", "YEAR(data)"), F("MONTH", "MONTH(data)"), F("DAY", "DAY(data)"),
            F("LEN", "LEN(texto)"), F("DATALENGTH", "DATALENGTH(expressão)"), F("LEFT", "LEFT(texto, n)"), F("RIGHT", "RIGHT(texto, n)"), F("SUBSTRING", "SUBSTRING(texto, início, tamanho)"),
            F("CHARINDEX", "CHARINDEX(procurado, texto [, início])"), F("PATINDEX", "PATINDEX('%padrão%', texto)"), F("REPLACE", "REPLACE(texto, de, para)"), F("REPLICATE", "REPLICATE(texto, n)"),
            F("UPPER", "UPPER(texto)"), F("LOWER", "LOWER(texto)"), F("LTRIM", "LTRIM(texto)"), F("RTRIM", "RTRIM(texto)"), F("TRIM", "TRIM(texto)"), F("CONCAT", "CONCAT(a, b, ...)"),
            F("CONCAT_WS", "CONCAT_WS(separador, a, b, ...)"), F("STRING_SPLIT", "STRING_SPLIT(texto, separador)"), F("STUFF", "STUFF(texto, início, tamanho, novo)"), F("REVERSE", "REVERSE(texto)"),
            F("QUOTENAME", "QUOTENAME(nome [, delimitador])"), F("TRANSLATE", "TRANSLATE(texto, de, para)"), F("SPACE", "SPACE(n)"), F("STR", "STR(número [, tamanho [, decimais]])"),
            F("ABS", "ABS(número)"), F("ROUND", "ROUND(número, casas)"), F("CEILING", "CEILING(número)"), F("FLOOR", "FLOOR(número)"), F("POWER", "POWER(base, expoente)"), F("SQRT", "SQRT(número)"),
            F("RAND", "RAND([semente])"), F("NEWID", "NEWID()"), F("NEWSEQUENTIALID", "NEWSEQUENTIALID()"),
            F("ROW_NUMBER", "ROW_NUMBER() OVER (ORDER BY ...)"), F("RANK", "RANK() OVER (ORDER BY ...)"), F("DENSE_RANK", "DENSE_RANK() OVER (ORDER BY ...)"), F("NTILE", "NTILE(n) OVER (ORDER BY ...)"),
            F("LAG", "LAG(expressão [, deslocamento [, padrão]]) OVER (...)"), F("LEAD", "LEAD(expressão [, deslocamento [, padrão]]) OVER (...)"),
            F("FIRST_VALUE", "FIRST_VALUE(expressão) OVER (...)"), F("LAST_VALUE", "LAST_VALUE(expressão) OVER (...)"),
            F("OBJECT_ID", "OBJECT_ID('nome' [, 'tipo'])"), F("OBJECT_NAME", "OBJECT_NAME(id)"), F("DB_NAME", "DB_NAME([id])"), F("DB_ID", "DB_ID(['nome'])"), F("SCHEMA_NAME", "SCHEMA_NAME([id])"),
            F("SUSER_SNAME", "SUSER_SNAME()"), F("USER_NAME", "USER_NAME()"), F("HOST_NAME", "HOST_NAME()"), F("APP_NAME", "APP_NAME()"), F("SCOPE_IDENTITY", "SCOPE_IDENTITY()"),
            F("ERROR_MESSAGE", "ERROR_MESSAGE()"), F("ERROR_NUMBER", "ERROR_NUMBER()"), F("ERROR_LINE", "ERROR_LINE()"), F("ERROR_PROCEDURE", "ERROR_PROCEDURE()"), F("ERROR_SEVERITY", "ERROR_SEVERITY()"),
            F("JSON_VALUE", "JSON_VALUE(json, caminho)"), F("JSON_QUERY", "JSON_QUERY(json, caminho)"), F("JSON_MODIFY", "JSON_MODIFY(json, caminho, valor)"), F("ISJSON", "ISJSON(texto)"),
            F("OPENJSON", "OPENJSON(json [, caminho])"), F("CHECKSUM", "CHECKSUM(expr, ...)"), F("HASHBYTES", "HASHBYTES('SHA2_256', valor)"), F("ISNUMERIC", "ISNUMERIC(expressão)"),
            F("GREATEST", "GREATEST(a, b, ...)"), F("LEAST", "LEAST(a, b, ...)"), F("CHOOSE", "CHOOSE(índice, a, b, ...)"), F("EXISTS", "EXISTS (subconsulta)")
        };

        private static KeyValuePair<string, string> F(string name, string sig) => new KeyValuePair<string, string>(name, sig);

        /// <summary>Palavras que começam uma instrução nova.</summary>
        public static readonly HashSet<string> StatementStarters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT","INSERT","UPDATE","DELETE","MERGE","WITH","DECLARE","SET","EXEC","EXECUTE","IF","ELSE","WHILE","BEGIN","END",
            "CREATE","ALTER","DROP","TRUNCATE","PRINT","RETURN","USE","RAISERROR","THROW","OPEN","FETCH","CLOSE","DEALLOCATE",
            "GRANT","REVOKE","DENY","BACKUP","RESTORE","DBCC","BREAK","CONTINUE","GOTO","WAITFOR","COMMIT","ROLLBACK","SAVE"
        };
    }
}
