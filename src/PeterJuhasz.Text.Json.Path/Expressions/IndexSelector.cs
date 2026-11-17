using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// An index selector, like <c>[0]</c> or <c>[-1]</c>: selects an element of an array by its index, counted from the end if negative.
/// </summary>
/// <param name="Index">The index; must be in the exact integer range of I-JSON, between -(2^53-1) and 2^53-1.</param>
public sealed record class IndexSelector(long Index) : JsonPathSelector
{
	/// <summary>
	/// The index, counted from the end of the array if negative, so -1 is the last element.
	/// </summary>
	public long Index { get; init => field = CheckRange(value); } = CheckRange(Index);

	internal static long CheckRange(long value, string? paramName = "value")
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(value, SyntaxFacts.MinSafeInteger, paramName);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(value, SyntaxFacts.MaxSafeInteger, paramName);
		return value;
	}

	internal override bool Select(ref QueryEvaluator evaluator, int next, JsonElement value, NormalizedPath? location)
	{
		if (value.ValueKind != JsonValueKind.Array)
		{
			return true;
		}

		var length = value.GetArrayLength();
		var index = Index >= 0 ? Index : length + Index;
		if (index < 0 || index >= length)
		{
			return true;
		}

		return evaluator.Visit(next, value[(int)index], location?.Append((int)index));
	}

	internal override void AppendTo(StringBuilder builder) => builder.Append(Index.ToString(CultureInfo.InvariantCulture));
}
