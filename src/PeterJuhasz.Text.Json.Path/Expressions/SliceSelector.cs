using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// An array slice selector, like <c>[1:5:2]</c> or <c>[::-1]</c>: selects the elements of an array from <see cref="Start"/> up to but not including <see cref="End"/>,
/// taking every <see cref="Step"/>th one, backwards if the step is negative. Negative bounds count from the end of the array, and bounds outside the array are clamped.
/// </summary>
/// <param name="Start">The index of the first element; the first element of the array if null, or the last one if the step is negative.</param>
/// <param name="End">The index after the last element; past the end of the array if null, or before its start if the step is negative.</param>
/// <param name="Step">The distance between the indices of the selected elements; 1 if null. A step of zero selects nothing.</param>
public sealed record class SliceSelector(long? Start = null, long? End = null, long? Step = null) : JsonPathSelector
{
	/// <summary>
	/// The index of the first element, counted from the end of the array if negative.
	/// </summary>
	public long? Start { get; init => field = CheckRange(value); } = CheckRange(Start);

	/// <summary>
	/// The index after the last element, counted from the end of the array if negative.
	/// </summary>
	public long? End { get; init => field = CheckRange(value); } = CheckRange(End);

	/// <summary>
	/// The distance between the indices of the selected elements.
	/// </summary>
	public long? Step { get; init => field = CheckRange(value); } = CheckRange(Step);

	private static long? CheckRange(long? value) => value is { } index ? IndexSelector.CheckRange(index) : null;

	/// <summary>
	/// Computes the bounds of the slice for an array of the given length, as defined by RFC 9535 (section 2.3.4.2.2).
	/// For a positive step, the indices from <paramref name="lower"/> up to but not including <paramref name="upper"/> are selected;
	/// for a negative one, the indices from <paramref name="upper"/> down to but not including <paramref name="lower"/>.
	/// </summary>
	internal void GetBounds(long length, long step, out long lower, out long upper)
	{
		if (step >= 0)
		{
			lower = Math.Clamp(Normalize(Start ?? 0, length), 0, length);
			upper = Math.Clamp(Normalize(End ?? length, length), 0, length);
		}
		else
		{
			upper = Math.Clamp(Normalize(Start ?? length - 1, length), -1, length - 1);
			lower = Math.Clamp(Normalize(End ?? -length - 1, length), -1, length - 1);
		}

		static long Normalize(long index, long length) => index >= 0 ? index : length + index;
	}

	internal override bool Select(ref QueryEvaluator evaluator, int next, JsonElement value, NormalizedPath? location)
	{
		var step = Step ?? 1;
		if (value.ValueKind != JsonValueKind.Array || step == 0)
		{
			return true;
		}

		var length = value.GetArrayLength();
		GetBounds(length, step, out var lower, out var upper);

		if (step > 0)
		{
			if (lower >= upper)
			{
				return true;
			}

			// the elements are enumerated rather than indexed, as indexing an array of objects or arrays walks it from the start
			var index = 0;
			foreach (var item in value.EnumerateArray())
			{
				if (index >= upper)
				{
					break;
				}

				if (index >= lower && (index - lower) % step == 0 && !evaluator.Visit(next, item, location?.Append(index)))
				{
					return false;
				}

				index++;
			}

			return true;
		}

		if (upper <= lower)
		{
			return true;
		}

		// the elements are collected first to walk them backwards; the array is cleared when returned, so it does not keep the document alive
		var count = (int)upper + 1;
		var items = ArrayPool<JsonElement>.Shared.Rent(count);
		try
		{
			var collected = 0;
			foreach (var item in value.EnumerateArray())
			{
				if (collected == count)
				{
					break;
				}

				items[collected++] = item;
			}

			for (var index = upper; lower < index; index += step)
			{
				if (!evaluator.Visit(next, items[index], location?.Append((int)index)))
				{
					return false;
				}
			}

			return true;
		}
		finally
		{
			ArrayPool<JsonElement>.Shared.Return(items, clearArray: true);
		}
	}

	internal override void AppendTo(StringBuilder builder)
	{
		if (Start is { } start)
		{
			builder.Append(start.ToString(CultureInfo.InvariantCulture));
		}

		builder.Append(':');
		if (End is { } end)
		{
			builder.Append(end.ToString(CultureInfo.InvariantCulture));
		}

		if (Step is { } step)
		{
			builder.Append(':').Append(step.ToString(CultureInfo.InvariantCulture));
		}
	}
}
