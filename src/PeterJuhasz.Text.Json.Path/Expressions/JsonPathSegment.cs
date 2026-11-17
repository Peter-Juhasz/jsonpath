using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// A segment of a query: applies its selectors to each node selected by the previous segment, or by the identifier for the first one.
/// A <see cref="ChildSegment"/> applies them to the node itself, a <see cref="DescendantSegment"/> to the node and all of its descendants.
/// </summary>
/// <param name="Selectors">The selectors, applied in order; the nodes they select are concatenated.</param>
public closed record class JsonPathSegment(ImmutableArray<JsonPathSelector> Selectors)
{
	/// <summary>
	/// The selectors, applied in order; a segment has at least one.
	/// </summary>
	public ImmutableArray<JsonPathSelector> Selectors { get; init => field = Guard.NotEmpty(value); } = Guard.NotEmpty(Selectors);

	/// <summary>
	/// Selects the nodes from a node, passing each to <paramref name="evaluator"/> to continue with the segment at <paramref name="next"/>.
	/// </summary>
	/// <returns>False if the evaluation should stop, as it has found as many nodes as it needed.</returns>
	internal abstract bool Select(ref QueryEvaluator evaluator, int next, JsonElement value, NormalizedPath? location);

	/// <summary>
	/// Applies the selectors in order to a single node.
	/// </summary>
	private protected bool SelectEach(ref QueryEvaluator evaluator, int next, JsonElement value, NormalizedPath? location)
	{
		foreach (var selector in Selectors)
		{
			if (!selector.Select(ref evaluator, next, value, location))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Returns the segment as written in a query.
	/// </summary>
	public sealed override string ToString()
	{
		using var pooled = StringBuilderPool.GetPooledObject(out var builder);
		AppendTo(builder);
		return builder.ToString();
	}

	internal abstract void AppendTo(StringBuilder builder);

	/// <summary>
	/// Writes the selectors in brackets, or a single name or wildcard in shorthand form after <paramref name="prefix"/>.
	/// </summary>
	private protected void AppendSelectors(StringBuilder builder, string prefix)
	{
		switch (Selectors)
		{
			case [NameSelector name] when SyntaxFacts.IsMemberNameShorthand(name.Name):
				builder.Append(prefix).Append(name.Name);
				return;

			case [WildcardSelector]:
				builder.Append(prefix).Append('*');
				return;
		}

		if (prefix.Length > 1)
		{
			builder.Append(prefix);
		}

		builder.Append('[');
		for (var i = 0; i < Selectors.Length; i++)
		{
			if (i > 0)
			{
				builder.Append(", ");
			}

			Selectors[i].AppendTo(builder);
		}

		builder.Append(']');
	}

	/// <summary>
	/// Compares the type and the selectors item by item.
	/// </summary>
	public virtual bool Equals(JsonPathSegment? other) => other is not null && EqualityContract == other.EqualityContract && Guard.ItemsEqual(Selectors, other.Selectors);

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(EqualityContract, Guard.GetItemsHashCode(Selectors));
}

/// <summary>
/// A child segment, like <c>['a', 'b']</c>, <c>.a</c> or <c>.*</c>: applies the selectors to the node.
/// </summary>
/// <param name="Selectors">The selectors, applied in order.</param>
public sealed record class ChildSegment(ImmutableArray<JsonPathSelector> Selectors) : JsonPathSegment(Selectors)
{
	internal override bool Select(ref QueryEvaluator evaluator, int next, JsonElement value, NormalizedPath? location) => SelectEach(ref evaluator, next, value, location);

	internal override void AppendTo(StringBuilder builder) => AppendSelectors(builder, ".");
}

/// <summary>
/// A descendant segment, like <c>..['a', 'b']</c>, <c>..a</c> or <c>..*</c>: applies the selectors to the node and to each of its descendants,
/// visiting a node before its descendants, and the elements of arrays and the members of objects in order.
/// </summary>
/// <param name="Selectors">The selectors, applied in order.</param>
public sealed record class DescendantSegment(ImmutableArray<JsonPathSelector> Selectors) : JsonPathSegment(Selectors)
{
	internal override bool Select(ref QueryEvaluator evaluator, int next, JsonElement value, NormalizedPath? location)
	{
		if (!SelectEach(ref evaluator, next, value, location))
		{
			return false;
		}

		switch (value.ValueKind)
		{
			case JsonValueKind.Array:
				var index = 0;
				foreach (var item in value.EnumerateArray())
				{
					if (!Select(ref evaluator, next, item, location?.Append(index)))
					{
						return false;
					}

					index++;
				}

				break;

			case JsonValueKind.Object:
				foreach (var property in value.EnumerateObject())
				{
					if (!Select(ref evaluator, next, property.Value, location?.Append(property.Name)))
					{
						return false;
					}
				}

				break;
		}

		return true;
	}

	internal override void AppendTo(StringBuilder builder) => AppendSelectors(builder, "..");
}
