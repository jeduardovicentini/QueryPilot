using System;
using System.Collections.Generic;
using System.Text;

namespace SqlTools.Sql
{
    public enum TokenKind
    {
        Identifier,
        QuotedIdentifier,
        Variable,
        String,
        Number,
        Comment,
        Dot,
        Comma,
        OpenParen,
        CloseParen,
        Semicolon,
        Star,
        Operator
    }

    public sealed class SqlToken
    {
        public TokenKind Kind { get; }
        public int Start { get; }
        public int Length { get; }
        public int End => Start + Length;
        /// <summary>Texto original (com colchetes/aspas).</summary>
        public string Text { get; }
        /// <summary>Para identificadores: o nome sem colchetes/aspas. Para os demais: o texto.</summary>
        public string Value { get; }
        /// <summary>String, comentário ou identificador entre colchetes sem o fechamento.</summary>
        public bool Unterminated { get; }

        public SqlToken(TokenKind kind, int start, string text, string value = null, bool unterminated = false)
        {
            Kind = kind;
            Start = start;
            Length = text.Length;
            Text = text;
            Value = value ?? text;
            Unterminated = unterminated;
        }

        public bool IsName => Kind == TokenKind.Identifier || Kind == TokenKind.QuotedIdentifier;

        /// <summary>Palavra (identificador sem colchetes) igual a alguma das palavras dadas.</summary>
        public bool Is(params string[] words)
        {
            if (Kind != TokenKind.Identifier) return false;
            foreach (var w in words)
                if (string.Equals(Text, w, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public override string ToString() => Kind + ":" + Text;
    }

    /// <summary>Tokenizer de T-SQL tolerante a texto incompleto (o usuário ainda está digitando).</summary>
    public static class SqlTokenizer
    {
        public static List<SqlToken> Tokenize(string text)
        {
            var tokens = new List<SqlToken>();
            if (string.IsNullOrEmpty(text)) return tokens;
            int i = 0, n = text.Length;

            while (i < n)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                int start = i;

                // comentário de linha
                if (c == '-' && i + 1 < n && text[i + 1] == '-')
                {
                    while (i < n && text[i] != '\n' && text[i] != '\r') i++;
                    tokens.Add(new SqlToken(TokenKind.Comment, start, text.Substring(start, i - start)));
                    continue;
                }

                // comentário de bloco (T-SQL permite aninhar)
                if (c == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    int depth = 0;
                    bool closed = false;
                    while (i < n)
                    {
                        if (text[i] == '/' && i + 1 < n && text[i + 1] == '*') { depth++; i += 2; continue; }
                        if (text[i] == '*' && i + 1 < n && text[i + 1] == '/')
                        {
                            depth--; i += 2;
                            if (depth == 0) { closed = true; break; }
                            continue;
                        }
                        i++;
                    }
                    tokens.Add(new SqlToken(TokenKind.Comment, start, text.Substring(start, i - start), null, !closed));
                    continue;
                }

                // string: '...' ou N'...'
                if (c == '\'' || ((c == 'N' || c == 'n') && i + 1 < n && text[i + 1] == '\''))
                {
                    if (c != '\'') i++;
                    i++;
                    bool closed = false;
                    while (i < n)
                    {
                        if (text[i] == '\'')
                        {
                            if (i + 1 < n && text[i + 1] == '\'') { i += 2; continue; }
                            i++; closed = true; break;
                        }
                        i++;
                    }
                    tokens.Add(new SqlToken(TokenKind.String, start, text.Substring(start, i - start), null, !closed));
                    continue;
                }

                // [identificador]
                if (c == '[')
                {
                    i++;
                    var sb = new StringBuilder();
                    bool closed = false;
                    while (i < n)
                    {
                        if (text[i] == ']')
                        {
                            if (i + 1 < n && text[i + 1] == ']') { sb.Append(']'); i += 2; continue; }
                            i++; closed = true; break;
                        }
                        sb.Append(text[i]); i++;
                    }
                    tokens.Add(new SqlToken(TokenKind.QuotedIdentifier, start, text.Substring(start, i - start), sb.ToString(), !closed));
                    continue;
                }

                // "identificador"
                if (c == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    bool closed = false;
                    while (i < n)
                    {
                        if (text[i] == '"')
                        {
                            if (i + 1 < n && text[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                            i++; closed = true; break;
                        }
                        sb.Append(text[i]); i++;
                    }
                    tokens.Add(new SqlToken(TokenKind.QuotedIdentifier, start, text.Substring(start, i - start), sb.ToString(), !closed));
                    continue;
                }

                // @variável, @@função
                if (c == '@')
                {
                    i++;
                    while (i < n && IsIdentChar(text[i])) i++;
                    tokens.Add(new SqlToken(TokenKind.Variable, start, text.Substring(start, i - start)));
                    continue;
                }

                // identificador (inclui #temp e ##global)
                if (IsIdentStart(c))
                {
                    i++;
                    while (i < n && IsIdentChar(text[i])) i++;
                    tokens.Add(new SqlToken(TokenKind.Identifier, start, text.Substring(start, i - start)));
                    continue;
                }

                if (char.IsDigit(c))
                {
                    i++;
                    while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '.')) i++;
                    tokens.Add(new SqlToken(TokenKind.Number, start, text.Substring(start, i - start)));
                    continue;
                }

                TokenKind kind;
                switch (c)
                {
                    case '.': kind = TokenKind.Dot; break;
                    case ',': kind = TokenKind.Comma; break;
                    case '(': kind = TokenKind.OpenParen; break;
                    case ')': kind = TokenKind.CloseParen; break;
                    case ';': kind = TokenKind.Semicolon; break;
                    case '*': kind = TokenKind.Star; break;
                    default: kind = TokenKind.Operator; break;
                }
                i++;
                tokens.Add(new SqlToken(kind, start, c.ToString()));
            }
            return tokens;
        }

        public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '#';
        public static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';
    }
}
