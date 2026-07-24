using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Evaluates column transform expressions written in the Smelter DSL.
    /// <para>
    /// The DSL supports function calls (<c>CONCAT</c>, <c>UPPER</c>, <c>LOWER</c>, <c>TRIM</c>,
    /// <c>LEFT</c>, <c>RIGHT</c>, <c>DATEPARSE</c>, <c>REPLACE</c>, <c>SPLIT</c>, <c>IF</c>,
    /// <c>COALESCE</c>, <c>STATIC</c>, <c>MAP</c>), bare column references, string literals
    /// (double-quoted, with <c>""</c> as the escape sequence for a literal quote), and numeric
    /// literals. Functions can be nested arbitrarily.
    /// </para>
    /// <para>
    /// The engine is stateless: a single instance can be reused across rows and chunks.
    /// </para>
    /// </summary>
    public class TransformEngine
    {
        /// <summary>
        /// Evaluates a single transform expression against one source row's values.
        /// </summary>
        /// <param name="expression">DSL expression string (e.g., <c>CONCAT(FirstName, " ", LastName)</c>).</param>
        /// <param name="rowValues">Raw column values for the current source row, keyed by header name.</param>
        /// <returns>
        /// The computed string value, or <c>null</c> when <paramref name="expression"/> is null or whitespace.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown on syntax errors, unknown functions, or unexpected tokens.
        /// </exception>
        public string Evaluate(string expression, Dictionary<string, string> rowValues)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;

            var tokens = Lex(expression);
            var parser = new Parser(tokens);
            var ast = parser.Parse();

            return EvaluateNode(ast, rowValues);
        }

        /// <summary>Recursively evaluates an AST node against the current row values.</summary>
        private string EvaluateNode(AstNode node, Dictionary<string, string> rowValues)
        {
            if (node is StringLiteralNode str) return str.Value;
            if (node is NumberLiteralNode num) return num.Value.ToString(CultureInfo.InvariantCulture);
            if (node is ColumnReferenceNode col)
            {
                rowValues.TryGetValue(col.ColumnName, out var val);
                return val?.ToString() ?? "";
            }
            if (node is FunctionCallNode func)
            {
                var args = func.Arguments.Select(a => EvaluateNode(a, rowValues)).ToList();
                return EvaluateFunction(func.FunctionName.ToUpperInvariant(), args);
            }

            throw new InvalidOperationException($"Unknown node type: {node.GetType().Name}");
        }

        /// <summary>
        /// Dispatches a resolved function call to its implementation. All function names are
        /// normalized to upper-case before dispatch so expressions are case-insensitive.
        /// </summary>
        private string EvaluateFunction(string name, List<string> args)
        {
            switch (name)
            {
                case "CONCAT": return string.Join("", args);
                case "UPPER": return args.Count > 0 ? args[0].ToUpperInvariant() : "";
                case "LOWER": return args.Count > 0 ? args[0].ToLowerInvariant() : "";
                case "TRIM": return args.Count > 0 ? args[0].Trim() : "";
                case "LEFT":
                    if (args.Count < 2 || !int.TryParse(args[1], out int leftLen)) return "";
                    return args[0].Length <= leftLen ? args[0] : args[0].Substring(0, leftLen);
                case "RIGHT":
                    if (args.Count < 2 || !int.TryParse(args[1], out int rightLen)) return "";
                    return args[0].Length <= rightLen ? args[0] : args[0].Substring(args[0].Length - rightLen);
                case "DATEPARSE":
                    if (args.Count < 2) return "";
                    if (DateTime.TryParseExact(args[0], args[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        return dt.ToString("o"); // ISO8601
                    return "";
                case "REPLACE":
                    if (args.Count < 3) return "";
                    return args[0].Replace(args[1], args[2]);
                case "SPLIT":
                    if (args.Count < 3 || !int.TryParse(args[2], out int idx)) return "";
                    var parts = args[0].Split(new[] { args[1] }, StringSplitOptions.None);
                    return idx >= 0 && idx < parts.Length ? parts[idx] : "";
                case "IF":
                    if (args.Count < 4) return "";
                    return args[0] == args[1] ? args[2] : args[3];
                case "COALESCE":
                    return args.FirstOrDefault(a => !string.IsNullOrEmpty(a)) ?? "";
                case "STATIC":
                    return args.Count > 0 ? args[0] : "";
                case "MAP":
                    if (args.Count < 2) return "";
                    var key = args[0];
                    var mapStr = args[1]; // Expected '{"NY":"New York"}'
                    // Very simple JSON extraction to avoid dependencies
                    var match = Regex.Match(mapStr, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(.*?)\"");
                    if (match.Success) return match.Groups[1].Value;
                    
                    var matchNum = Regex.Match(mapStr, $"\"{Regex.Escape(key)}\"\\s*:\\s*([0-9.]+)");
                    if (matchNum.Success) return matchNum.Groups[1].Value;
                    
                    return "";
                default:
                    throw new InvalidOperationException($"Unknown function: {name}");
            }
        }

        #region Lexer & Parser

        /// <summary>Token types recognized by the DSL lexer.</summary>
        private enum TokenType { Identifier, String, Number, Comma, ParenOpen, ParenClose, EOF }

        /// <summary>A single lexical token produced by the <see cref="Lex"/> method.</summary>
        private class Token
        {
            public TokenType Type { get; }
            public string Value { get; }
            public Token(TokenType type, string value = null) { Type = type; Value = value; }
        }

        /// <summary>
        /// Tokenizes the DSL expression string into a flat token list terminated by an EOF token.
        /// String literals use double-quote delimiters; <c>""</c> inside a string is treated as
        /// an escaped literal double-quote (same convention as CSV).
        /// </summary>
        private List<Token> Lex(string input)
        {
            var tokens = new List<Token>();
            int i = 0;

            while (i < input.Length)
            {
                char c = input[i];

                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '(') { tokens.Add(new Token(TokenType.ParenOpen)); i++; continue; }
                if (c == ')') { tokens.Add(new Token(TokenType.ParenClose)); i++; continue; }
                if (c == ',') { tokens.Add(new Token(TokenType.Comma)); i++; continue; }

                if (c == '"')
                {
                    i++;
                    var sb = new System.Text.StringBuilder();
                    while (i < input.Length)
                    {
                        if (input[i] == '"')
                        {
                            if (i + 1 < input.Length && input[i + 1] == '"') // escaped quote ""
                            {
                                sb.Append('"');
                                i += 2;
                            }
                            else
                            {
                                i++; // closing quote
                                break;
                            }
                        }
                        else
                        {
                            sb.Append(input[i]);
                            i++;
                        }
                    }
                    tokens.Add(new Token(TokenType.String, sb.ToString()));
                    continue;
                }

                if (char.IsDigit(c) || c == '-')
                {
                    int start = i;
                    while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.' || input[i] == '-')) i++;
                    tokens.Add(new Token(TokenType.Number, input.Substring(start, i - start)));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_')) i++;
                    tokens.Add(new Token(TokenType.Identifier, input.Substring(start, i - start)));
                    continue;
                }

                throw new InvalidOperationException($"Unexpected character at position {i}: {c}");
            }

            tokens.Add(new Token(TokenType.EOF));
            return tokens;
        }

        /// <summary>Base class for all AST nodes produced by the <see cref="Parser"/>.</summary>
        private abstract class AstNode { }

        /// <summary>A double-quoted string literal in the expression.</summary>
        private class StringLiteralNode : AstNode { public string Value { get; set; } }

        /// <summary>A numeric literal; stored as <see cref="double"/> for generality.</summary>
        private class NumberLiteralNode : AstNode { public double Value { get; set; } }

        /// <summary>A bare identifier that refers to a source column by name.</summary>
        private class ColumnReferenceNode : AstNode { public string ColumnName { get; set; } }

        /// <summary>A function call with zero or more argument sub-expressions.</summary>
        private class FunctionCallNode : AstNode
        {
            public string FunctionName { get; set; }
            public List<AstNode> Arguments { get; set; } = new List<AstNode>();
        }

        /// <summary>
        /// Recursive-descent parser that consumes a token list and produces an AST.
        /// The grammar is: Expression = StringLiteral | NumberLiteral | FunctionCall | ColumnRef.
        /// </summary>
        private class Parser
        {
            private readonly List<Token> _tokens;
            private int _pos;

            public Parser(List<Token> tokens)
            {
                _tokens = tokens;
                _pos = 0;
            }

            private Token Peek() => _pos < _tokens.Count ? _tokens[_pos] : _tokens.Last();
            private Token Consume() => _pos < _tokens.Count ? _tokens[_pos++] : _tokens.Last();

            /// <summary>
            /// Parses the full token stream and returns the root AST node.
            /// Throws <see cref="InvalidOperationException"/> if tokens remain after the expression.
            /// </summary>
            public AstNode Parse()
            {
                var node = ParseExpression();
                if (Peek().Type != TokenType.EOF)
                    throw new InvalidOperationException("Expected EOF");
                return node;
            }

            private AstNode ParseExpression()
            {
                var token = Peek();

                if (token.Type == TokenType.String)
                {
                    Consume();
                    return new StringLiteralNode { Value = token.Value };
                }
                
                if (token.Type == TokenType.Number)
                {
                    Consume();
                    return new NumberLiteralNode { Value = double.Parse(token.Value, CultureInfo.InvariantCulture) };
                }

                if (token.Type == TokenType.Identifier)
                {
                    var id = Consume().Value;
                    if (Peek().Type == TokenType.ParenOpen)
                    {
                        // Function call
                        Consume(); // (
                        var func = new FunctionCallNode { FunctionName = id };
                        
                        if (Peek().Type != TokenType.ParenClose)
                        {
                            func.Arguments.Add(ParseExpression());
                            while (Peek().Type == TokenType.Comma)
                            {
                                Consume(); // ,
                                func.Arguments.Add(ParseExpression());
                            }
                        }

                        if (Peek().Type != TokenType.ParenClose)
                            throw new InvalidOperationException($"Expected ')' after arguments for {id}");
                        Consume(); // )

                        return func;
                    }
                    else
                    {
                        // Column reference
                        return new ColumnReferenceNode { ColumnName = id };
                    }
                }

                throw new InvalidOperationException($"Unexpected token: {token.Type}");
            }
        }
        #endregion
    }
}
