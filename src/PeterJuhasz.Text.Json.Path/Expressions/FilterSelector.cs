using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// A filter selector, like <c>[?@.price &lt; 10]</c>: selects the elements of an array, or the values of the members of an object, for which the expression is true.
/// </summary>
/// <param name="Expression">The logical expression, evaluated with each child as the current node, <c>@</c>; a query on its own tests whether it selects any node.</param>
public sealed record class FilterSelector(FilterExpression Expression) : JsonPathSelector
{
	/// <summary>
	/// The logical expression; it must be of <see cref="FilterExpressionType.Logical"/> or <see cref="FilterExpressionType.Nodes"/> type.
	/// </summary>
	public FilterExpression Expression { get; init => field = FilterExpression.CheckLogical(value); } = FilterExpression.CheckLogical(Expression);

	internal override bool Select(ref QueryEvaluator evaluator, int next, JsonElement value, NormalizedPath? location)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.Array:
				var index = 0;
				foreach (var item in value.EnumerateArray())
				{
					if (Expression.IsTrue(new FilterContext(evaluator.Root, item)) && !evaluator.Visit(next, item, location?.Append(index)))
					{
						return false;
					}

					index++;
				}

				break;

			case JsonValueKind.Object:
				foreach (var property in value.EnumerateObject())
				{
					var member = property.Value;
					if (Expression.IsTrue(new FilterContext(evaluator.Root, member)) && !evaluator.Visit(next, member, location?.Append(property.Name)))
					{
						return false;
					}
				}

				break;
		}

		return true;
	}

	internal override void AppendTo(StringBuilder builder)
	{
		builder.Append('?');
		Expression.AppendTo(builder);
	}
}
