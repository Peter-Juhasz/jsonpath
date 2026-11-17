using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Text;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// The operators of comparisons.
/// </summary>
public enum ComparisonOperator
{
	/// <summary>
	/// <c>==</c>
	/// </summary>
	Equal,

	/// <summary>
	/// <c>!=</c>
	/// </summary>
	NotEqual,

	/// <summary>
	/// <c>&lt;</c>
	/// </summary>
	LessThan,

	/// <summary>
	/// <c>&lt;=</c>
	/// </summary>
	LessThanOrEqual,

	/// <summary>
	/// <c>&gt;</c>
	/// </summary>
	GreaterThan,

	/// <summary>
	/// <c>&gt;=</c>
	/// </summary>
	GreaterThanOrEqual,
}

/// <summary>
/// A comparison, like <c>@.price &lt; 10</c>, as defined by RFC 9535 (section 2.3.5.2.2). Equal values are of the same kind: numbers equal by their value, so <c>1 == 1.0</c>,
/// strings, arrays and objects by their contents, and Nothing, what a query that selects no node produces, only to Nothing.
/// Only numbers and strings are ordered, strings by their Unicode scalar values; other values are neither less nor greater than each other.
/// </summary>
/// <param name="Left">The left operand: a literal, a singular query or a function returning a value.</param>
/// <param name="Operator">The operator.</param>
/// <param name="Right">The right operand: a literal, a singular query or a function returning a value.</param>
public sealed record class ComparisonExpression(FilterExpression Left, ComparisonOperator Operator, FilterExpression Right) : FilterExpression
{
	/// <summary>
	/// The left operand.
	/// </summary>
	public FilterExpression Left { get; init => field = CheckComparable(value); } = CheckComparable(Left);

	/// <summary>
	/// The right operand.
	/// </summary>
	public FilterExpression Right { get; init => field = CheckComparable(value); } = CheckComparable(Right);

	private static FilterExpression CheckComparable(FilterExpression expression)
	{
		ArgumentNullException.ThrowIfNull(expression);
		return expression.IsComparable ? expression : throw new ArgumentException($"'{expression}' cannot be compared; only literals, singular queries and functions returning a value can.", nameof(expression));
	}

	/// <inheritdoc/>
	public override FilterExpressionType Type => FilterExpressionType.Logical;

	internal override bool IsTrue(in FilterContext context) => FilterValueComparer.Compare(Left.Evaluate(context), Operator, Right.Evaluate(context));

	internal override void AppendTo(StringBuilder builder)
	{
		Left.AppendTo(builder);
		builder.Append(Operator switch
		{
			ComparisonOperator.Equal => " == ",
			ComparisonOperator.NotEqual => " != ",
			ComparisonOperator.LessThan => " < ",
			ComparisonOperator.LessThanOrEqual => " <= ",
			ComparisonOperator.GreaterThan => " > ",
			ComparisonOperator.GreaterThanOrEqual => " >= ",
			_ => throw new InvalidOperationException($"Unknown comparison operator '{Operator}'."),
		});
		Right.AppendTo(builder);
	}
}
