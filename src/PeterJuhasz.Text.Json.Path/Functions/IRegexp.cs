using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PeterJuhasz.Text.Json.Path.Functions;

/// <summary>
/// Translates I-Regexp patterns (RFC 9485), the interoperable regular expressions of <c>match()</c> and <c>search()</c>, to .NET regular expressions,
/// rejecting anything that is not I-Regexp, like <c>\d</c>, lookarounds or backreferences, as RFC 9535 requires invalid patterns to match nothing.
/// </summary>
/// <remarks>
/// I-Regexp matches Unicode code points, while .NET matches UTF-16 code units, so the translation matches characters outside the Basic Multilingual Plane as surrogate pairs:
/// <c>.</c>, negated classes and ranges match them as a whole and never a lone half. Unicode categories like <c>\p{L}</c> only know characters of the Basic Multilingual Plane
/// in .NET, so characters outside it are taken as not belonging to any category: they never match <c>\p{…}</c> and always match <c>\P{…}</c>.
/// As in the mappings to ECMAScript and PCRE of RFC 9485, <c>^</c> and <c>$</c> outside classes match the start and the end of the string.
/// The regular expressions use <see cref="RegexOptions.NonBacktracking"/>, so their matching time is linear in the length of the input, whatever the pattern.
/// </remarks>
internal ref struct IRegexp
{
	/// <summary>
	/// How deeply groups may be nested, so that the recursion of the translator stays bounded for pathological input.
	/// </summary>
	internal const int MaxDepth = 64;

	/// <summary>
	/// How many compiled patterns are cached; when the cache is full, it is cleared.
	/// </summary>
	internal const int CacheCapacity = 512;

	private static readonly ConcurrentDictionary<string, Regex?> AnchoredCache = new(StringComparer.Ordinal);

	private static readonly ConcurrentDictionary<string, Regex?> UnanchoredCache = new(StringComparer.Ordinal);

	private const int MaxBmp = 0xFFFF;

	private const int MinSupplementary = 0x10000;

	private const int MaxCodePoint = 0x10FFFF;

	/// <summary>
	/// Gets the regular expression that matches whole strings, as <c>match()</c> does; null if the pattern is not valid.
	/// </summary>
	public static Regex? GetAnchored(string pattern) => GetOrCreate(AnchoredCache, pattern, anchored: true);

	/// <summary>
	/// Gets the regular expression that matches substrings, as <c>search()</c> does; null if the pattern is not valid.
	/// </summary>
	public static Regex? GetUnanchored(string pattern) => GetOrCreate(UnanchoredCache, pattern, anchored: false);

	private static Regex? GetOrCreate(ConcurrentDictionary<string, Regex?> cache, string pattern, bool anchored)
	{
		if (cache.TryGetValue(pattern, out var regex))
		{
			return regex;
		}

		regex = Create(pattern, anchored);
		if (cache.Count >= CacheCapacity)
		{
			cache.Clear();
		}

		cache.TryAdd(pattern, regex);
		return regex;
	}

	/// <summary>
	/// Compiles a pattern; null if it is not valid I-Regexp, or too large for .NET to compile.
	/// </summary>
	internal static Regex? Create(string pattern, bool anchored)
	{
		if (!TryTranslate(pattern, out var translated))
		{
			return null;
		}

		try
		{
			return new Regex(anchored ? $@"\A(?:{translated})\z" : translated, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
		{
			return null;
		}
	}

	/// <summary>
	/// Translates an I-Regexp pattern to a .NET regular expression, returning false if it is not valid.
	/// </summary>
	internal static bool TryTranslate(ReadOnlySpan<char> pattern, [NotNullWhen(true)] out string? translated)
	{
		using var pooled = StringBuilderPool.GetPooledObject(out var builder);
		var translator = new IRegexp(pattern, builder);
		if (translator.TryReadRegexp() && translator._position == pattern.Length)
		{
			translated = builder.ToString();
			return true;
		}

		translated = null;
		return false;
	}

	private IRegexp(ReadOnlySpan<char> pattern, StringBuilder output)
	{
		_pattern = pattern;
		_output = output;
	}

	private readonly ReadOnlySpan<char> _pattern;

	private readonly StringBuilder _output;

	private int _position;

	private int _depth;

	private readonly bool AtEnd => _position >= _pattern.Length;

	private readonly char Peek(int offset = 0) => _position + offset < _pattern.Length ? _pattern[_position + offset] : '\0';

	/// <summary>
	/// <c>i-regexp = branch *( "|" branch )</c>
	/// </summary>
	private bool TryReadRegexp()
	{
		if (!TryReadBranch())
		{
			return false;
		}

		while (!AtEnd && Peek() == '|')
		{
			_position++;
			_output.Append('|');
			if (!TryReadBranch())
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// <c>branch = *piece</c>
	/// </summary>
	private bool TryReadBranch()
	{
		while (!AtEnd && Peek() is not ('|' or ')'))
		{
			if (!TryReadPiece())
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// <c>piece = atom [ quantifier ]</c>; every atom is written as a single unit of the .NET syntax, so the quantifier applies to all of it.
	/// </summary>
	private bool TryReadPiece()
	{
		if (!TryReadAtom())
		{
			return false;
		}

		if (AtEnd)
		{
			return true;
		}

		switch (Peek())
		{
			case '*' or '+' or '?':
				_output.Append(Peek());
				_position++;
				return true;

			case '{':
				return TryReadRangeQuantifier();

			default:
				return true;
		}
	}

	/// <summary>
	/// <c>range-quantifier = "{" QuantExact [ "," [ QuantExact ] ] "}"</c>
	/// </summary>
	private bool TryReadRangeQuantifier()
	{
		_position++;
		if (!TryReadQuantity(out var min))
		{
			return false;
		}

		_output.Append('{').Append(min.ToString(CultureInfo.InvariantCulture));
		if (Peek() == ',' && !AtEnd)
		{
			_position++;
			_output.Append(',');
			if (char.IsAsciiDigit(Peek()))
			{
				if (!TryReadQuantity(out var max) || max < min)
				{
					return false;
				}

				_output.Append(max.ToString(CultureInfo.InvariantCulture));
			}
		}

		if (AtEnd || Peek() != '}')
		{
			return false;
		}

		_position++;
		_output.Append('}');
		return true;
	}

	private bool TryReadQuantity(out int value)
	{
		var start = _position;
		while (!AtEnd && char.IsAsciiDigit(Peek()))
		{
			_position++;
		}

		return int.TryParse(_pattern[start.._position], NumberStyles.None, CultureInfo.InvariantCulture, out value);
	}

	/// <summary>
	/// <c>atom = NormalChar / charClass / ( "(" i-regexp ")" )</c>, where <c>charClass = "." / SingleCharEsc / charClassEsc / charClassExpr</c>.
	/// </summary>
	private bool TryReadAtom()
	{
		switch (Peek())
		{
			case '(':
				if (++_depth > MaxDepth)
				{
					return false;
				}

				_position++;
				_output.Append("(?:");
				if (!TryReadRegexp() || AtEnd || Peek() != ')')
				{
					return false;
				}

				_position++;
				_output.Append(')');
				_depth--;
				return true;

			case '.':
				_position++;
				AppendClass([('\n', '\n'), ('\r', '\r')], [], negated: true);
				return true;

			case '[':
				return TryReadClassExpression();

			case '\\':
				_position++;
				if (TryReadCategoryEscape(out var category, out var isValid))
				{
					AppendClass([], [category], negated: false);
					return true;
				}

				if (!isValid || !TryReadSingleCharEscape(out var escaped))
				{
					return false;
				}

				AppendCodePoint(escaped);
				return true;

			case ')' or '*' or '+' or '?' or ']' or '{' or '|' or '}':
				return false;

			// the grammar makes these normal characters, but the mappings to ECMAScript and PCRE of RFC 9485 (sections 5.3 and 5.4) keep them,
			// where they are anchors, and the Compliance Test Suite of RFC 9535 expects them to be
			case '^':
				_position++;
				_output.Append(@"\A");
				return true;

			case '$':
				_position++;
				_output.Append(@"\z");
				return true;

			default:
				if (!TryReadCodePoint(out var codePoint))
				{
					return false;
				}

				AppendCodePoint(codePoint);
				return true;
		}
	}

	/// <summary>
	/// <c>charClassExpr = "[" [ "^" ] ( "-" / CCE1 ) *CCE1 [ "-" ] "]"</c>, where <c>CCE1 = ( CCchar [ "-" CCchar ] ) / charClassEsc</c>.
	/// </summary>
	private bool TryReadClassExpression()
	{
		_position++;
		var negated = false;
		if (Peek() == '^' && !AtEnd)
		{
			negated = true;
			_position++;
		}

		var ranges = new List<(int Low, int High)>();
		var categories = new List<Category>();

		// a hyphen is a character of its own at the start, or right before the end
		if (Peek() == '-' && !AtEnd)
		{
			ranges.Add(('-', '-'));
			_position++;
		}

		while (true)
		{
			if (AtEnd)
			{
				return false;
			}

			switch (Peek())
			{
				case ']':
					if (ranges.Count == 0 && categories.Count == 0)
					{
						return false;
					}

					_position++;
					AppendClass(ranges, categories, negated);
					return true;

				case '-':
					if (Peek(1) != ']' || _position + 1 >= _pattern.Length)
					{
						return false;
					}

					ranges.Add(('-', '-'));
					_position++;
					continue;

				case '\\' when Peek(1) is 'p' or 'P':
					_position++;
					if (!TryReadCategoryEscape(out var category, out _))
					{
						return false;
					}

					categories.Add(category);
					continue;
			}

			if (!TryReadClassChar(out var low))
			{
				return false;
			}

			var high = low;
			if (Peek() == '-' && Peek(1) != ']' && _position + 1 < _pattern.Length)
			{
				_position++;
				if (!TryReadClassChar(out high) || high < low)
				{
					return false;
				}
			}

			ranges.Add((low, high));
		}
	}

	/// <summary>
	/// <c>CCchar</c>: a character in a class other than <c>-</c>, <c>[</c>, <c>\</c> and <c>]</c>, or a single character escape.
	/// </summary>
	private bool TryReadClassChar(out int codePoint)
	{
		codePoint = 0;
		switch (Peek())
		{
			case '\\':
				_position++;
				return TryReadSingleCharEscape(out codePoint);

			case '-' or '[' or ']':
				return false;

			default:
				return !AtEnd && TryReadCodePoint(out codePoint);
		}
	}

	/// <summary>
	/// <c>SingleCharEsc</c>, after the backslash: one of <c>()*+-.?[\]^{|}</c>, or <c>n</c>, <c>r</c> and <c>t</c> for line feed, carriage return and tab.
	/// </summary>
	private bool TryReadSingleCharEscape(out int codePoint)
	{
		codePoint = Peek() switch
		{
			'(' or ')' or '*' or '+' or '-' or '.' or '?' or '[' or '\\' or ']' or '^' or '{' or '|' or '}' => Peek(),
			'n' => '\n',
			'r' => '\r',
			't' => '\t',
			_ => -1,
		};

		if (codePoint < 0 || AtEnd)
		{
			return false;
		}

		_position++;
		return true;
	}

	/// <summary>
	/// <c>catEsc = "\p{" charProp "}"</c> and <c>complEsc = "\P{" charProp "}"</c>, after the backslash, where <c>charProp</c> is a general category or a group of them.
	/// </summary>
	/// <param name="category">The category that was read.</param>
	/// <param name="isValid">False if the escape starts as a category escape but is not a valid one.</param>
	/// <returns>Whether a category escape was read.</returns>
	private bool TryReadCategoryEscape(out Category category, out bool isValid)
	{
		category = default;
		isValid = true;
		if (Peek() is not ('p' or 'P') || AtEnd)
		{
			return false;
		}

		var complement = Peek() == 'P';
		var start = _position + 2;
		var end = start < _pattern.Length ? _pattern[start..].IndexOf('}') : -1;
		if (Peek(1) != '{' || end < 0 || !IsCategoryName(_pattern.Slice(start, end)))
		{
			isValid = false;
			return false;
		}

		category = new Category(_pattern.Slice(start, end).ToString(), complement);
		_position = start + end + 1;
		return true;
	}

	/// <summary>
	/// <c>IsCategory</c>: the general categories that I-Regexp supports, and their groups by first letter.
	/// </summary>
	private static bool IsCategoryName(ReadOnlySpan<char> name) => name switch
	{
		[var group] => group is 'L' or 'M' or 'N' or 'P' or 'Z' or 'S' or 'C',
		['L', var c] => c is 'l' or 'm' or 'o' or 't' or 'u',
		['M', var c] => c is 'c' or 'e' or 'n',
		['N', var c] => c is 'd' or 'l' or 'o',
		['P', var c] => c is 'c' or 'd' or 'e' or 'f' or 'i' or 'o' or 's',
		['Z', var c] => c is 'l' or 'p' or 's',
		['S', var c] => c is 'c' or 'k' or 'm' or 'o',
		['C', var c] => c is 'c' or 'f' or 'n' or 'o',
		_ => false,
	};

	/// <summary>
	/// Reads a character as a code point, combining surrogate pairs; a lone surrogate is not a valid character.
	/// </summary>
	private bool TryReadCodePoint(out int codePoint)
	{
		var c = _pattern[_position];
		if (char.IsHighSurrogate(c) && _position + 1 < _pattern.Length && char.IsLowSurrogate(_pattern[_position + 1]))
		{
			codePoint = char.ConvertToUtf32(c, _pattern[_position + 1]);
			_position += 2;
			return true;
		}

		codePoint = c;
		if (char.IsSurrogate(c))
		{
			return false;
		}

		_position++;
		return true;
	}

	/// <summary>
	/// Writes a character to match literally, escaped, so that no character is mistaken for syntax.
	/// </summary>
	private readonly void AppendCodePoint(int codePoint)
	{
		if (codePoint <= MaxBmp)
		{
			AppendChar(_output, codePoint);
		}
		else
		{
			_output.Append("(?:");
			AppendSurrogatePair(codePoint);
			_output.Append(')');
		}
	}

	/// <summary>
	/// Writes a class: the characters of the Basic Multilingual Plane as a .NET class that never matches a surrogate,
	/// and the characters outside it as alternatives of surrogate pairs.
	/// </summary>
	private readonly void AppendClass(List<(int Low, int High)> ranges, List<Category> categories, bool negated)
	{
		using var pooled = StringBuilderPool.GetPooledObject(out var bmp);
		var supplementary = new List<(int Low, int High)>();
		foreach (var (low, high) in ranges)
		{
			if (low <= MaxBmp)
			{
				AppendChar(bmp, low);
				if (high > low)
				{
					bmp.Append('-');
					AppendChar(bmp, Math.Min(high, MaxBmp));
				}
			}

			if (high >= MinSupplementary)
			{
				supplementary.Add((Math.Max(low, MinSupplementary), high));
			}
		}

		foreach (var category in categories)
		{
			bmp.Append(category.IsComplement ? @"\P{" : @"\p{").Append(category.Name).Append('}');

			// characters outside the Basic Multilingual Plane are taken as not belonging to any category
			if (category.IsComplement)
			{
				supplementary.Add((MinSupplementary, MaxCodePoint));
			}
		}

		supplementary = Merge(supplementary);
		if (negated)
		{
			supplementary = Complement(supplementary);
		}

		_output.Append("(?:");
		var any = false;
		if (negated)
		{
			_output.Append("[^").Append(bmp).Append(@"\uD800-\uDFFF]");
			any = true;
		}
		else if (bmp.Length > 0)
		{
			_output.Append('[').Append(bmp).Append(@"-[\uD800-\uDFFF]]");
			any = true;
		}

		foreach (var (low, high) in supplementary)
		{
			if (any)
			{
				_output.Append('|');
			}

			AppendSupplementaryRange(low, high);
			any = true;
		}

		if (!any)
		{
			_output.Append(@"[^\s\S]");
		}

		_output.Append(')');
	}

	/// <summary>
	/// Writes a range of characters outside the Basic Multilingual Plane as alternatives of surrogate pairs.
	/// </summary>
	private readonly void AppendSupplementaryRange(int low, int high)
	{
		var (lowHigh, lowLow) = ToSurrogates(low);
		var (highHigh, highLow) = ToSurrogates(high);
		if (lowHigh == highHigh)
		{
			AppendChar(_output, lowHigh);
			AppendCharRange(lowLow, highLow);
			return;
		}

		AppendChar(_output, lowHigh);
		AppendCharRange(lowLow, 0xDFFF);
		if (highHigh - lowHigh > 1)
		{
			_output.Append('|');
			AppendCharRange(lowHigh + 1, highHigh - 1);
			AppendCharRange(0xDC00, 0xDFFF);
		}

		_output.Append('|');
		AppendChar(_output, highHigh);
		AppendCharRange(0xDC00, highLow);
	}

	private readonly void AppendSurrogatePair(int codePoint)
	{
		var (high, low) = ToSurrogates(codePoint);
		AppendChar(_output, high);
		AppendChar(_output, low);
	}

	private readonly void AppendCharRange(int low, int high)
	{
		_output.Append('[');
		AppendChar(_output, low);
		if (high > low)
		{
			_output.Append('-');
			AppendChar(_output, high);
		}

		_output.Append(']');
	}

	private static (int High, int Low) ToSurrogates(int codePoint) => (0xD800 + ((codePoint - MinSupplementary) >> 10), 0xDC00 + ((codePoint - MinSupplementary) & 0x3FF));

	private static void AppendChar(StringBuilder builder, int c) => builder.Append(CultureInfo.InvariantCulture, $@"\u{c:X4}");

	/// <summary>
	/// Sorts the ranges and merges the ones that overlap or touch.
	/// </summary>
	private static List<(int Low, int High)> Merge(List<(int Low, int High)> ranges)
	{
		ranges.Sort();
		var merged = new List<(int Low, int High)>(ranges.Count);
		foreach (var range in ranges)
		{
			if (merged.Count > 0 && range.Low <= merged[^1].High + 1)
			{
				merged[^1] = (merged[^1].Low, Math.Max(merged[^1].High, range.High));
			}
			else
			{
				merged.Add(range);
			}
		}

		return merged;
	}

	/// <summary>
	/// Returns the characters outside the Basic Multilingual Plane that are not in the merged ranges.
	/// </summary>
	private static List<(int Low, int High)> Complement(List<(int Low, int High)> ranges)
	{
		var complement = new List<(int Low, int High)>();
		var next = MinSupplementary;
		foreach (var (low, high) in ranges)
		{
			if (low > next)
			{
				complement.Add((next, low - 1));
			}

			next = high + 1;
		}

		if (next <= MaxCodePoint)
		{
			complement.Add((next, MaxCodePoint));
		}

		return complement;
	}

	private readonly record struct Category(string Name, bool IsComplement);
}
