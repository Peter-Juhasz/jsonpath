using PeterJuhasz.Text.Json.Path.Expressions;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path;

/// <summary>
/// Parses JSONPath queries as defined by RFC 9535 into <see cref="JsonPathQuery"/> trees, checking that filters are well-typed (section 2.4.3):
/// only singular queries and functions returning values can be compared, and the arguments of functions must match the types of their parameters.
/// </summary>
/// <remarks>
/// The text is read as a span from start to end; the only lookahead is for the whitespace before an operator or a segment, which is given back if none follows.
/// </remarks>
internal ref struct JsonPathParser(ReadOnlySpan<char> text)
{
	/// <summary>
	/// How deeply filters, parentheses and function calls may be nested, so that the recursion of the parser and the evaluation stays bounded for pathological input.
	/// </summary>
	internal const int MaxDepth = 64;

	private readonly ReadOnlySpan<char> _text = text;

	private int _position;

	private int _depth;

	private string? _error;

	private int _errorPosition;

	private static readonly ImmutableArray<JsonPathSelector> Wildcard = [WildcardSelector.Instance];

	/// <summary>
	/// Parses a query, returning false with a message that tells why and where, if it is not valid.
	/// </summary>
	public static bool TryParse(ReadOnlySpan<char> query, [NotNullWhen(true)] out JsonPathQuery? result, [NotNullWhen(false)] out string? error)
	{
		var parser = new JsonPathParser(query);
		if (parser.TryReadQuery(out result))
		{
			error = null;
			return true;
		}

		error = $"'{query}' is not a valid JSONPath query: {parser._error} at position {parser._errorPosition}.";
		return false;
	}

	private readonly bool AtEnd => _position >= _text.Length;

	private readonly char Peek(int offset = 0) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

	private readonly bool PeekIs(char c) => !AtEnd && _text[_position] == c;

	private bool TryRead(char c)
	{
		if (PeekIs(c))
		{
			_position++;
			return true;
		}

		return false;
	}

	private bool TryRead(string token)
	{
		if (_text[_position..].StartsWith(token, StringComparison.Ordinal))
		{
			_position += token.Length;
			return true;
		}

		return false;
	}

	private void SkipBlanks()
	{
		while (!AtEnd && SyntaxFacts.IsBlank(_text[_position]))
		{
			_position++;
		}
	}

	private bool Fail(string error) => FailAt(_position, error);

	private bool FailAt(int position, string error)
	{
		if (_error is null)
		{
			_error = error;
			_errorPosition = position;
		}

		return false;
	}

	private readonly string Describe() => AtEnd ? "end of query" : $"'{_text[_position]}'";

	private bool TryEnter()
	{
		if (++_depth > MaxDepth)
		{
			return Fail($"nesting is deeper than {MaxDepth} levels");
		}

		return true;
	}

	/// <summary>
	/// <c>jsonpath-query = root-identifier segments</c>, with nothing before or after it, not even whitespace.
	/// </summary>
	private bool TryReadQuery([NotNullWhen(true)] out JsonPathQuery? query)
	{
		query = null;
		if (!TryRead('$'))
		{
			return Fail($"expected '$' but found {Describe()}");
		}

		if (!TryReadSegments(out var segments))
		{
			return false;
		}

		if (!AtEnd)
		{
			return Fail($"unexpected {Describe()}");
		}

		query = new JsonPathQuery(segments);
		return true;
	}

	/// <summary>
	/// <c>segments = *(S segment)</c>; the whitespace before something that is not a segment is given back.
	/// </summary>
	private bool TryReadSegments(out ImmutableArray<JsonPathSegment> segments)
	{
		segments = default;
		using var builder = new PooledArrayBuilder<JsonPathSegment>();
		while (true)
		{
			var start = _position;
			SkipBlanks();
			if (PeekIs('['))
			{
				if (!TryReadBracketedSelection(out var selectors))
				{
					return false;
				}

				builder.Add(new ChildSegment(selectors));
			}
			else if (PeekIs('.'))
			{
				_position++;
				var descendant = TryRead('.');
				if (!TryReadDotted(descendant, out var selectors))
				{
					return false;
				}

				builder.Add(descendant ? new DescendantSegment(selectors) : new ChildSegment(selectors));
			}
			else
			{
				_position = start;
				break;
			}
		}

		segments = builder.ToImmutableArray();
		return true;
	}

	/// <summary>
	/// Reads what follows <c>.</c> or <c>..</c>: a wildcard or a member name shorthand, or for <c>..</c> also a bracketed selection, without whitespace in between.
	/// </summary>
	private bool TryReadDotted(bool descendant, out ImmutableArray<JsonPathSelector> selectors)
	{
		selectors = default;
		if (descendant && PeekIs('['))
		{
			return TryReadBracketedSelection(out selectors);
		}

		if (TryRead('*'))
		{
			selectors = Wildcard;
			return true;
		}

		if (TryReadMemberNameShorthand(out var name))
		{
			selectors = [new NameSelector(name)];
			return true;
		}

		return Fail(descendant ? $"expected a member name, '*' or '[' after '..' but found {Describe()}" : $"expected a member name or '*' after '.' but found {Describe()}");
	}

	/// <summary>
	/// <c>member-name-shorthand = name-first *name-char</c>
	/// </summary>
	private bool TryReadMemberNameShorthand([NotNullWhen(true)] out string? name)
	{
		var start = _position;
		while (!AtEnd)
		{
			var c = _text[_position];
			if (char.IsHighSurrogate(c) && char.IsLowSurrogate(Peek(1)))
			{
				_position += 2;
			}
			else if (_position == start ? SyntaxFacts.IsNameFirst(c) : SyntaxFacts.IsNameChar(c))
			{
				_position++;
			}
			else
			{
				break;
			}
		}

		name = _position > start ? _text[start.._position].ToString() : null;
		return name is not null;
	}

	/// <summary>
	/// <c>bracketed-selection = "[" S selector *(S "," S selector) S "]"</c>
	/// </summary>
	private bool TryReadBracketedSelection(out ImmutableArray<JsonPathSelector> selectors)
	{
		selectors = default;
		_position++;
		using var builder = new PooledArrayBuilder<JsonPathSelector>();
		while (true)
		{
			SkipBlanks();
			if (!TryReadSelector(out var selector))
			{
				return false;
			}

			builder.Add(selector);
			SkipBlanks();
			if (TryRead(']'))
			{
				break;
			}

			if (!TryRead(','))
			{
				return Fail($"expected ',' or ']' but found {Describe()}");
			}
		}

		selectors = builder.ToImmutableArray();
		return true;
	}

	/// <summary>
	/// <c>selector = name-selector / wildcard-selector / slice-selector / index-selector / filter-selector</c>
	/// </summary>
	private bool TryReadSelector([NotNullWhen(true)] out JsonPathSelector? selector)
	{
		selector = null;
		switch (AtEnd ? '\0' : Peek())
		{
			case '\'' or '"':
				if (!TryReadStringLiteral(out var name))
				{
					return false;
				}

				selector = new NameSelector(name);
				return true;

			case '*':
				_position++;
				selector = WildcardSelector.Instance;
				return true;

			case '?':
				_position++;
				SkipBlanks();
				if (!TryEnter() || !TryReadLogicalExpression(out var expression))
				{
					return false;
				}

				_depth--;
				selector = new FilterSelector(expression);
				return true;

			case ':' or '-' or (>= '0' and <= '9'):
				return TryReadIndexOrSlice(out selector);

			default:
				return Fail($"expected a selector but found {Describe()}");
		}
	}

	/// <summary>
	/// <c>index-selector = int</c> and <c>slice-selector = [start S] ":" S [end S] [":" [S step]]</c>
	/// </summary>
	private bool TryReadIndexOrSlice([NotNullWhen(true)] out JsonPathSelector? selector)
	{
		selector = null;
		long? start = null;
		if (!PeekIs(':'))
		{
			if (!TryReadInteger(out var index))
			{
				return false;
			}

			var afterIndex = _position;
			SkipBlanks();
			if (!PeekIs(':'))
			{
				_position = afterIndex;
				selector = new IndexSelector(index);
				return true;
			}

			start = index;
		}

		_position++;
		SkipBlanks();
		long? end = null;
		if (IsIntegerStart())
		{
			if (!TryReadInteger(out var value))
			{
				return false;
			}

			end = value;
			SkipBlanks();
		}

		long? step = null;
		if (TryRead(':'))
		{
			SkipBlanks();
			if (IsIntegerStart())
			{
				if (!TryReadInteger(out var value))
				{
					return false;
				}

				step = value;
			}
		}

		selector = new SliceSelector(start, end, step);
		return true;
	}

	private readonly bool IsIntegerStart() => !AtEnd && (Peek() == '-' || char.IsAsciiDigit(Peek()));

	/// <summary>
	/// <c>int = "0" / (["-"] DIGIT1 *DIGIT)</c>, in the exact integer range of I-JSON.
	/// </summary>
	private bool TryReadInteger(out long value)
	{
		value = 0;
		var start = _position;
		var negative = TryRead('-');
		if (!char.IsAsciiDigit(Peek()) || AtEnd)
		{
			return Fail($"expected a digit but found {Describe()}");
		}

		if (TryRead('0'))
		{
			if (negative)
			{
				return FailAt(start, "'-0' is not a valid integer");
			}

			if (char.IsAsciiDigit(Peek()) && !AtEnd)
			{
				return FailAt(start, "integers must not have leading zeros");
			}

			return true;
		}

		var magnitude = 0L;
		while (!AtEnd && char.IsAsciiDigit(Peek()))
		{
			magnitude = (magnitude * 10) + (Peek() - '0');
			if (magnitude > SyntaxFacts.MaxSafeInteger)
			{
				return FailAt(start, $"integers must be between {SyntaxFacts.MinSafeInteger} and {SyntaxFacts.MaxSafeInteger}");
			}

			_position++;
		}

		value = negative ? -magnitude : magnitude;
		return true;
	}

	/// <summary>
	/// <c>string-literal</c>, in double or single quotes, with escapes decoded. A quote of the other kind may appear unescaped, but not escaped.
	/// </summary>
	private bool TryReadStringLiteral([NotNullWhen(true)] out string? value)
	{
		value = null;
		var start = _position;
		var quote = _text[_position++];
		var contentStart = _position;

		// strings without escapes, the usual case, are sliced from the text
		var length = _text[_position..].IndexOfAny(quote, '\\');
		if (length >= 0 && _text[_position + length] == quote)
		{
			if (!TryCheckUnescaped(_position + length))
			{
				return false;
			}

			value = _text.Slice(contentStart, length).ToString();
			_position++;
			return true;
		}

		using var pooled = StringBuilderPool.GetPooledObject(out var builder);
		while (true)
		{
			var next = _text[_position..].IndexOfAny(quote, '\\');
			if (next < 0)
			{
				return FailAt(start, "the string literal is not closed");
			}

			var segmentStart = _position;
			if (!TryCheckUnescaped(_position + next))
			{
				return false;
			}

			builder.Append(_text[segmentStart.._position]);
			if (_text[_position] == quote)
			{
				_position++;
				value = builder.ToString();
				return true;
			}

			if (!TryReadEscape(quote, builder))
			{
				return false;
			}
		}
	}

	/// <summary>
	/// Checks the characters up to <paramref name="end"/> against <c>unescaped</c>: control characters must be escaped, and surrogates must be in pairs.
	/// </summary>
	private bool TryCheckUnescaped(int end)
	{
		for (; _position < end; _position++)
		{
			var c = _text[_position];
			if (c < ' ')
			{
				return Fail($"control character U+{(int)c:X4} must be escaped in string literals");
			}

			if (char.IsHighSurrogate(c) && _position + 1 < end && char.IsLowSurrogate(_text[_position + 1]))
			{
				_position++;
			}
			else if (char.IsSurrogate(c))
			{
				return Fail("lone surrogates are not allowed in string literals");
			}
		}

		return true;
	}

	/// <summary>
	/// <c>ESC escapable</c>: <c>\b</c>, <c>\f</c>, <c>\n</c>, <c>\r</c>, <c>\t</c>, <c>\/</c>, <c>\\</c>, the quote of the literal, or <c>\uXXXX</c>, where a high surrogate must be followed by a low one.
	/// </summary>
	private bool TryReadEscape(char quote, StringBuilder builder)
	{
		var start = _position;
		_position++;
		var c = Peek();
		if (AtEnd)
		{
			return FailAt(start, "the string literal is not closed");
		}

		_position++;
		switch (c)
		{
			case 'b':
				builder.Append('\b');
				return true;

			case 'f':
				builder.Append('\f');
				return true;

			case 'n':
				builder.Append('\n');
				return true;

			case 'r':
				builder.Append('\r');
				return true;

			case 't':
				builder.Append('\t');
				return true;

			case '/' or '\\':
				builder.Append(c);
				return true;

			case 'u':
				if (!TryReadHex(out var unit))
				{
					return FailAt(start, "expected four hexadecimal digits after '\\u'");
				}

				if (char.IsLowSurrogate(unit))
				{
					return FailAt(start, "a low surrogate must follow a high surrogate");
				}

				if (char.IsHighSurrogate(unit))
				{
					if (!TryRead("\\u") || !TryReadHex(out var low) || !char.IsLowSurrogate(low))
					{
						return FailAt(start, "a high surrogate must be followed by an escaped low surrogate");
					}

					builder.Append(unit).Append(low);
					return true;
				}

				builder.Append(unit);
				return true;

			default:
				if (c == quote)
				{
					builder.Append(c);
					return true;
				}

				return FailAt(start, $"'\\{c}' is not a valid escape");
		}
	}

	private bool TryReadHex(out char value)
	{
		value = '\0';
		if (_position + 4 > _text.Length || !ushort.TryParse(_text.Slice(_position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
		{
			return false;
		}

		_position += 4;
		value = (char)code;
		return true;
	}

	/// <summary>
	/// <c>logical-expr</c> as the expression of a filter selector, which must be of logical or nodes type.
	/// </summary>
	private bool TryReadLogicalExpression([NotNullWhen(true)] out FilterExpression? expression)
	{
		var start = _position;
		return TryReadOr(out expression, out _) && TryCheckLogical(expression, start);
	}

	private bool TryCheckLogical(FilterExpression expression, int position) => expression.Type is not FilterExpressionType.Value || FailAt(position, expression is LiteralExpression
		? "a literal must be compared with something"
		: $"the result of '{(expression as FunctionExpression)?.Function.Name}()' must be compared with something");

	/// <summary>
	/// <c>logical-or-expr = logical-and-expr *(S "||" S logical-and-expr)</c>
	/// </summary>
	/// <param name="expression">The expression.</param>
	/// <param name="isBare">Whether the expression is a literal, query or function call on its own, not in parentheses or combined with operators,
	/// which function arguments of value and nodes type must be.</param>
	private bool TryReadOr([NotNullWhen(true)] out FilterExpression? expression, out bool isBare)
	{
		var start = _position;
		if (!TryReadAnd(out expression, out isBare))
		{
			return false;
		}

		var end = _position;
		SkipBlanks();
		if (!TryRead("||"))
		{
			_position = end;
			return true;
		}

		if (!TryCheckLogical(expression, start))
		{
			return false;
		}

		using var operands = new PooledArrayBuilder<FilterExpression>();
		operands.Add(expression);
		do
		{
			SkipBlanks();
			var operandStart = _position;
			if (!TryReadAnd(out var operand, out _) || !TryCheckLogical(operand, operandStart))
			{
				return false;
			}

			operands.Add(operand);
			end = _position;
			SkipBlanks();
		}
		while (TryRead("||"));

		_position = end;
		expression = new OrExpression(operands.ToImmutableArray());
		isBare = false;
		return true;
	}

	/// <summary>
	/// <c>logical-and-expr = basic-expr *(S "&amp;&amp;" S basic-expr)</c>
	/// </summary>
	private bool TryReadAnd([NotNullWhen(true)] out FilterExpression? expression, out bool isBare)
	{
		var start = _position;
		if (!TryReadBasic(out expression, out isBare))
		{
			return false;
		}

		var end = _position;
		SkipBlanks();
		if (!TryRead("&&"))
		{
			_position = end;
			return true;
		}

		if (!TryCheckLogical(expression, start))
		{
			return false;
		}

		using var operands = new PooledArrayBuilder<FilterExpression>();
		operands.Add(expression);
		do
		{
			SkipBlanks();
			var operandStart = _position;
			if (!TryReadBasic(out var operand, out _) || !TryCheckLogical(operand, operandStart))
			{
				return false;
			}

			operands.Add(operand);
			end = _position;
			SkipBlanks();
		}
		while (TryRead("&&"));

		_position = end;
		expression = new AndExpression(operands.ToImmutableArray());
		isBare = false;
		return true;
	}

	/// <summary>
	/// <c>basic-expr = paren-expr / comparison-expr / test-expr</c>, where <c>paren-expr = [logical-not-op S] "(" S logical-expr S ")"</c>,
	/// <c>test-expr = [logical-not-op S] (filter-query / function-expr)</c> and <c>comparison-expr = comparable S comparison-op S comparable</c>.
	/// A literal on its own is read as well, for function arguments; the callers check where it is not allowed.
	/// </summary>
	private bool TryReadBasic([NotNullWhen(true)] out FilterExpression? expression, out bool isBare)
	{
		isBare = false;
		if (TryRead('!'))
		{
			SkipBlanks();
			var operandStart = _position;
			FilterExpression? operand;
			if (PeekIs('('))
			{
				if (!TryReadParenthesized(out operand))
				{
					expression = null;
					return false;
				}
			}
			else
			{
				if (!TryReadPrimary(out operand))
				{
					expression = null;
					return false;
				}

				if (operand.Type is FilterExpressionType.Value)
				{
					expression = null;
					return FailAt(operandStart, "'!' must be followed by a query, a function returning a logical value or nodes, or an expression in parentheses");
				}
			}

			expression = new NotExpression(operand);
			return true;
		}

		if (PeekIs('('))
		{
			return TryReadParenthesized(out expression);
		}

		var start = _position;
		if (!TryReadPrimary(out expression))
		{
			return false;
		}

		var end = _position;
		SkipBlanks();
		if (!TryReadComparisonOperator(out var @operator))
		{
			_position = end;
			isBare = true;
			return true;
		}

		if (!TryCheckComparable(expression, start))
		{
			return false;
		}

		SkipBlanks();
		var rightStart = _position;
		if (!TryReadPrimary(out var right) || !TryCheckComparable(right, rightStart))
		{
			return false;
		}

		expression = new ComparisonExpression(expression, @operator, right);
		return true;
	}

	private bool TryCheckComparable(FilterExpression expression, int position) => expression.IsComparable || FailAt(position, expression switch
	{
		QueryExpression => "only singular queries can be compared",
		FunctionExpression function => $"the result of '{function.Function.Name}()' is not a value and cannot be compared",
		_ => "only literals, singular queries and functions returning a value can be compared",
	});

	private bool TryReadComparisonOperator(out ComparisonOperator @operator)
	{
		(@operator, var length) = (Peek(), Peek(1)) switch
		{
			('=', '=') => (ComparisonOperator.Equal, 2),
			('!', '=') => (ComparisonOperator.NotEqual, 2),
			('<', '=') => (ComparisonOperator.LessThanOrEqual, 2),
			('>', '=') => (ComparisonOperator.GreaterThanOrEqual, 2),
			('<', _) => (ComparisonOperator.LessThan, 1),
			('>', _) => (ComparisonOperator.GreaterThan, 1),
			_ => (default, 0),
		};

		if (length == 0 || AtEnd)
		{
			return false;
		}

		_position += length;
		return true;
	}

	/// <summary>
	/// <c>"(" S logical-expr S ")"</c>
	/// </summary>
	private bool TryReadParenthesized([NotNullWhen(true)] out FilterExpression? expression)
	{
		expression = null;
		_position++;
		if (!TryEnter())
		{
			return false;
		}

		SkipBlanks();
		if (!TryReadLogicalExpression(out expression))
		{
			return false;
		}

		SkipBlanks();
		if (!TryRead(')'))
		{
			return Fail($"expected ')' but found {Describe()}");
		}

		_depth--;
		return true;
	}

	/// <summary>
	/// A literal, a query (<c>filter-query</c>) or a function call (<c>function-expr</c>): the operands of comparisons and tests.
	/// </summary>
	private bool TryReadPrimary([NotNullWhen(true)] out FilterExpression? expression)
	{
		expression = null;
		var start = _position;
		switch (AtEnd ? '\0' : Peek())
		{
			case '$' or '@':
				var relative = _text[_position++] == '@';
				if (!TryReadSegments(out var segments))
				{
					return false;
				}

				expression = new QueryExpression(new JsonPathQuery(segments, relative));
				return true;

			case '\'' or '"':
				if (!TryReadStringLiteral(out var text))
				{
					return false;
				}

				expression = LiteralExpression.String(text);
				return true;

			case '-' or (>= '0' and <= '9'):
				return TryReadNumber(out expression);

			case >= 'a' and <= 'z':
				while (!AtEnd && SyntaxFacts.IsFunctionNameChar(Peek(), first: _position == start))
				{
					_position++;
				}

				var name = _text[start.._position];
				if (PeekIs('('))
				{
					return TryReadFunction(name, start, out expression);
				}

				expression = name switch
				{
					"true" => LiteralExpression.True,
					"false" => LiteralExpression.False,
					"null" => LiteralExpression.Null,
					_ => null,
				};

				return expression is not null || FailAt(start, $"unexpected '{name}'; function names must be followed by '('");

			default:
				return Fail($"expected a query, a literal or a function call but found {Describe()}");
		}
	}

	/// <summary>
	/// <c>number = (int / "-0") [ frac ] [ exp ]</c>
	/// </summary>
	private bool TryReadNumber([NotNullWhen(true)] out FilterExpression? expression)
	{
		expression = null;
		var start = _position;
		TryRead('-');
		if (TryRead('0'))
		{
			if (!AtEnd && char.IsAsciiDigit(Peek()))
			{
				return FailAt(start, "numbers must not have leading zeros");
			}
		}
		else if (!SkipDigits())
		{
			return Fail($"expected a digit but found {Describe()}");
		}

		if (TryRead('.') && !SkipDigits())
		{
			return Fail($"expected a digit after '.' but found {Describe()}");
		}

		if (TryRead('e') || TryRead('E'))
		{
			if (!TryRead('+'))
			{
				TryRead('-');
			}

			if (!SkipDigits())
			{
				return Fail($"expected a digit in the exponent but found {Describe()}");
			}
		}

		// the number syntax of JSONPath is the same as that of JSON
		expression = new LiteralExpression(JsonElement.Parse(_text[start.._position]));
		return true;
	}

	private bool SkipDigits()
	{
		var start = _position;
		while (!AtEnd && char.IsAsciiDigit(Peek()))
		{
			_position++;
		}

		return _position > start;
	}

	/// <summary>
	/// <c>function-expr = function-name "(" S [function-argument *(S "," S function-argument)] S ")"</c>, where the arguments must match the types of the parameters.
	/// </summary>
	private bool TryReadFunction(ReadOnlySpan<char> name, int start, [NotNullWhen(true)] out FilterExpression? expression)
	{
		expression = null;
		if (!JsonPathFunction.TryGet(name, out var function))
		{
			return FailAt(start, $"unknown function '{name}'");
		}

		_position++;
		if (!TryEnter())
		{
			return false;
		}

		var parameters = function.ParameterTypes;
		using var arguments = new PooledArrayBuilder<FilterExpression>();
		SkipBlanks();
		if (!PeekIs(')'))
		{
			while (true)
			{
				SkipBlanks();
				var argumentStart = _position;
				if (!TryReadOr(out var argument, out var isBare))
				{
					return false;
				}

				if (arguments.Count >= parameters.Length)
				{
					return FailAt(argumentStart, $"'{function.Name}()' takes {parameters.Length} argument(s)");
				}

				var parameter = parameters[arguments.Count];
				if (!(parameter is FilterExpressionType.Logical || isBare) || !argument.IsConvertibleTo(parameter))
				{
					return FailAt(argumentStart, $"argument {arguments.Count + 1} of '{function.Name}()' must be {Describe(parameter)}");
				}

				arguments.Add(argument);
				SkipBlanks();
				if (!TryRead(','))
				{
					break;
				}
			}
		}

		if (!TryRead(')'))
		{
			return Fail($"expected ',' or ')' but found {Describe()}");
		}

		if (arguments.Count != parameters.Length)
		{
			return FailAt(start, $"'{function.Name}()' takes {parameters.Length} argument(s)");
		}

		_depth--;
		expression = new FunctionExpression(function, arguments.ToImmutableArray());
		return true;
	}

	private static string Describe(FilterExpressionType type) => type switch
	{
		FilterExpressionType.Value => "a value: a literal, a singular query or a function returning a value",
		FilterExpressionType.Logical => "a logical expression or a query",
		_ => "a query",
	};
}
