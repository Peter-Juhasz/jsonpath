using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// The wildcard selector, <c>*</c>: selects all elements of an array, or the values of all members of an object, in order.
/// </summary>
public sealed record class WildcardSelector : JsonPathSelector
{
	private WildcardSelector()
	{
	}

	/// <summary>
	/// The wildcard selector; it has no state, so a single instance is shared.
	/// </summary>
	public static WildcardSelector Instance { get; } = new();

	internal override bool Select(ref QueryEvaluator evaluator, int next, JsonElement value, NormalizedPath? location)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.Array:
				var index = 0;
				foreach (var item in value.EnumerateArray())
				{
					if (!evaluator.Visit(next, item, location?.Append(index)))
					{
						return false;
					}

					index++;
				}

				break;

			case JsonValueKind.Object:
				foreach (var property in value.EnumerateObject())
				{
					if (!evaluator.Visit(next, property.Value, location?.Append(property.Name)))
					{
						return false;
					}
				}

				break;
		}

		return true;
	}

	internal override void AppendTo(StringBuilder builder) => builder.Append('*');
}
