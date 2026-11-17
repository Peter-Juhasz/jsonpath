using System.Globalization;
using System.Text;

namespace PeterJuhasz.Text.Json.Path;

/// <summary>
/// The character classes and literal forms of the JSONPath grammar of RFC 9535, shared by the parser and by writing queries back as text.
/// </summary>
internal static class SyntaxFacts
{
	/// <summary>
	/// The largest integer that indices and slice bounds may have, as they must be in the exact integer range of I-JSON (RFC 7493).
	/// </summary>
	public const long MaxSafeInteger = (1L << 53) - 1;

	/// <summary>
	/// The smallest integer that indices and slice bounds may have.
	/// </summary>
	public const long MinSafeInteger = -MaxSafeInteger;

	/// <summary>
	/// Whitespace, which may appear between the tokens of a query: space, horizontal tab, line feed and carriage return.
	/// </summary>
	public static bool IsBlank(char c) => c is ' ' or '\t' or '\n' or '\r';

	/// <summary>
	/// <c>name-first</c>: the characters a member name shorthand like <c>.name</c> may start with. Surrogates only count as a pair, which the caller checks.
	/// </summary>
	public static bool IsNameFirst(char c) => char.IsAsciiLetter(c) || c == '_' || (c >= 0x80 && !char.IsSurrogate(c));

	/// <summary>
	/// <c>name-char</c>: the characters a member name shorthand may continue with.
	/// </summary>
	public static bool IsNameChar(char c) => IsNameFirst(c) || char.IsAsciiDigit(c);

	/// <summary>
	/// <c>function-name-first</c> and <c>function-name-char</c>: function names are lowercase letters, then lowercase letters, digits and underscores.
	/// </summary>
	public static bool IsFunctionNameChar(char c, bool first) => char.IsAsciiLetterLower(c) || (!first && (c == '_' || char.IsAsciiDigit(c)));

	/// <summary>
	/// Checks whether a member name can be written in the shorthand form, like <c>.name</c>, instead of <c>['name']</c>.
	/// </summary>
	public static bool IsMemberNameShorthand(string name)
	{
		if (name.Length == 0)
		{
			return false;
		}

		for (var i = 0; i < name.Length; i++)
		{
			var c = name[i];
			if (char.IsHighSurrogate(c) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
			{
				i++;
				continue;
			}

			if (i == 0 ? !IsNameFirst(c) : !IsNameChar(c))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Writes a string as a single-quoted string literal, escaped as in normalized paths: apostrophes, backslashes and control characters are escaped,
	/// everything else is written as it is.
	/// </summary>
	public static void AppendStringLiteral(StringBuilder builder, string value)
	{
		builder.Append('\'');
		for (var i = 0; i < value.Length; i++)
		{
			var c = value[i];
			switch (c)
			{
				case '\'':
					builder.Append("\\'");
					break;

				case '\\':
					builder.Append("\\\\");
					break;

				case '\b':
					builder.Append("\\b");
					break;

				case '\f':
					builder.Append("\\f");
					break;

				case '\n':
					builder.Append("\\n");
					break;

				case '\r':
					builder.Append("\\r");
					break;

				case '\t':
					builder.Append("\\t");
					break;

				case < ' ':
					AppendUnicodeEscape(builder, c);
					break;

				case >= '\uD800' and <= '\uDBFF' when i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]):
					builder.Append(c).Append(value[++i]);
					break;

				// a lone surrogate cannot be written as it is, as it is not a character on its own
				case >= '\uD800' and <= '\uDFFF':
					AppendUnicodeEscape(builder, c);
					break;

				default:
					builder.Append(c);
					break;
			}
		}

		builder.Append('\'');
	}

	private static void AppendUnicodeEscape(StringBuilder builder, char c) => builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
}
