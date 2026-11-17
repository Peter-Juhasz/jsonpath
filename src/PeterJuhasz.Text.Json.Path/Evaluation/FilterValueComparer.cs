using PeterJuhasz.Text.Json.Path.Expressions;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Evaluation;

/// <summary>
/// Compares values as defined by RFC 9535 (section 2.3.5.2.2).
/// </summary>
internal static class FilterValueComparer
{
	public static bool Compare(in FilterValue left, ComparisonOperator @operator, in FilterValue right) => @operator switch
	{
		ComparisonOperator.Equal => AreEqual(left, right),
		ComparisonOperator.NotEqual => !AreEqual(left, right),
		ComparisonOperator.LessThan => IsLess(left, right),
		ComparisonOperator.LessThanOrEqual => IsLess(left, right) || AreEqual(left, right),
		ComparisonOperator.GreaterThan => IsLess(right, left),
		ComparisonOperator.GreaterThanOrEqual => IsLess(right, left) || AreEqual(left, right),
		_ => throw new InvalidOperationException($"Unknown comparison operator '{@operator}'."),
	};

	/// <summary>
	/// Nothing equals only Nothing; numbers equal by their value; other values equal if they are of the same kind with equal contents.
	/// </summary>
	public static bool AreEqual(in FilterValue left, in FilterValue right)
	{
		if (left.IsNothing || right.IsNothing)
		{
			return left.IsNothing && right.IsNothing;
		}

		if (left.Kind == FilterValueKind.Integer || right.Kind == FilterValueKind.Integer)
		{
			return left.IsNumber && right.IsNumber && CompareNumbers(left, right) == 0;
		}

		return AreEqual(left.Element, right.Element);
	}

	public static bool AreEqual(JsonElement left, JsonElement right)
	{
		var kind = left.ValueKind;
		if (kind != right.ValueKind)
		{
			return false;
		}

		return kind switch
		{
			JsonValueKind.String => StringsEqual(left, right),

			// numbers are compared by their exact decimal value, and objects regardless of the order of their members
			JsonValueKind.Number or JsonValueKind.Array or JsonValueKind.Object => JsonElement.DeepEquals(left, right),

			// true, false and null are equal to themselves
			_ => true,
		};
	}

	/// <summary>
	/// Only numbers and strings are ordered; any other pair of values is neither less nor greater than each other.
	/// </summary>
	public static bool IsLess(in FilterValue left, in FilterValue right)
	{
		if (left.IsNumber && right.IsNumber)
		{
			return CompareNumbers(left, right) < 0;
		}

		if (left.IsString && right.IsString)
		{
			return CompareStrings(left.Element, right.Element) < 0;
		}

		return false;
	}

	/// <summary>
	/// Compares two numbers: exactly as integers or decimals where they fit, otherwise as doubles.
	/// </summary>
	private static int CompareNumbers(in FilterValue left, in FilterValue right)
	{
		if (TryGetInt64(left, out var leftInteger) && TryGetInt64(right, out var rightInteger))
		{
			return leftInteger.CompareTo(rightInteger);
		}

		// decimals round tiny values to zero, so equal decimals are compared again as doubles; rounding keeps the order, so different ones decide
		if (TryGetDecimal(left, out var leftDecimal) && TryGetDecimal(right, out var rightDecimal) && leftDecimal != rightDecimal)
		{
			return leftDecimal.CompareTo(rightDecimal);
		}

		return GetDouble(left).CompareTo(GetDouble(right));
	}

	private static bool TryGetInt64(in FilterValue value, out long result)
	{
		if (value.Kind == FilterValueKind.Integer)
		{
			result = value.Integer;
			return true;
		}

		return value.Element.TryGetInt64(out result);
	}

	private static bool TryGetDecimal(in FilterValue value, out decimal result)
	{
		if (value.Kind == FilterValueKind.Integer)
		{
			result = value.Integer;
			return true;
		}

		return value.Element.TryGetDecimal(out result);
	}

	private static double GetDouble(in FilterValue value) => value.Kind == FilterValueKind.Integer ? value.Integer : GetDouble(value.Element);

	/// <summary>
	/// Gets a number as a double, as infinity if it is too large, which <see cref="JsonElement.TryGetDouble(out double)"/> refuses.
	/// </summary>
	internal static double GetDouble(JsonElement number) => number.TryGetDouble(out var result)
		? result
		: double.Parse(number.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);

	/// <summary>
	/// Gets the raw UTF-8 bytes of a string between the quotes, still escaped.
	/// </summary>
	private static ReadOnlySpan<byte> GetRawString(JsonElement value) => JsonMarshal.GetRawUtf8Value(value)[1..^1];

	private static bool StringsEqual(JsonElement left, JsonElement right)
	{
		var leftRaw = GetRawString(left);
		var rightRaw = GetRawString(right);
		var leftEscaped = leftRaw.Contains((byte)'\\');
		var rightEscaped = rightRaw.Contains((byte)'\\');
		return (leftEscaped, rightEscaped) switch
		{
			(false, false) => leftRaw.SequenceEqual(rightRaw),
			(false, true) => right.ValueEquals(leftRaw),
			(true, false) => left.ValueEquals(rightRaw),
			(true, true) => left.ValueEquals(right.GetString()),
		};
	}

	/// <summary>
	/// Compares two strings by their Unicode scalar values: UTF-8 bytes sort the same way, so unescaped strings are compared as they are in the document.
	/// </summary>
	private static int CompareStrings(JsonElement left, JsonElement right)
	{
		var leftRaw = GetRawString(left);
		var rightRaw = GetRawString(right);
		if (!leftRaw.Contains((byte)'\\') && !rightRaw.Contains((byte)'\\'))
		{
			return leftRaw.SequenceCompareTo(rightRaw);
		}

		return CompareByCodePoint(left.GetString()!, right.GetString()!);
	}

	/// <summary>
	/// Compares two strings by their code points: UTF-16 sorts surrogates, which encode the code points above U+FFFF, below U+E000 to U+FFFF,
	/// so the code units are shifted to sort surrogates last.
	/// </summary>
	internal static int CompareByCodePoint(string left, string right)
	{
		var length = Math.Min(left.Length, right.Length);
		for (var i = 0; i < length; i++)
		{
			if (left[i] != right[i])
			{
				return Fix(left[i]).CompareTo(Fix(right[i]));
			}
		}

		return left.Length.CompareTo(right.Length);

		static int Fix(char c) => c switch
		{
			>= '\uE000' => c - 0x800,
			>= '\uD800' => c + 0x2000,
			_ => c,
		};
	}

	/// <summary>
	/// Counts the Unicode scalar values of a string, which <c>length()</c> returns: the UTF-8 bytes that start a character for unescaped strings.
	/// </summary>
	internal static long CountScalarValues(JsonElement value)
	{
		var raw = GetRawString(value);
		if (!raw.Contains((byte)'\\'))
		{
			var count = 0L;
			foreach (var b in raw)
			{
				if ((b & 0xC0) != 0x80)
				{
					count++;
				}
			}

			return count;
		}

		var text = value.GetString()!;
		var scalars = 0L;
		foreach (var _ in text.EnumerateRunes())
		{
			scalars++;
		}

		return scalars;
	}
}
