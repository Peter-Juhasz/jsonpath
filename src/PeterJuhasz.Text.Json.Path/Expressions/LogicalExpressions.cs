using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Collections.Immutable;
using System.Text;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// Logical OR, like <c>@.a || @.b</c>: true if any of the operands is true; evaluated from left to right, stopping at the first true operand.
/// </summary>
/// <param name="Operands">The logical expressions, in the order they are written.</param>
public sealed record class OrExpression(ImmutableArray<FilterExpression> Operands) : FilterExpression
{
	/// <summary>
	/// The logical expressions; there is at least one.
	/// </summary>
	public ImmutableArray<FilterExpression> Operands { get; init => field = CheckOperands(value); } = CheckOperands(Operands);

	internal static ImmutableArray<FilterExpression> CheckOperands(ImmutableArray<FilterExpression> operands)
	{
		foreach (var operand in Guard.NotEmpty(operands))
		{
			CheckLogical(operand, nameof(operands));
		}

		return operands;
	}

	/// <summary>
	/// Returns the operands of an OR expression, or the expression itself otherwise, so <c>a | b | c</c> makes a single expression.
	/// </summary>
	internal static ImmutableArray<FilterExpression> Flatten(FilterExpression expression) => expression is OrExpression disjunction ? disjunction.Operands : [expression];

	/// <inheritdoc/>
	public override FilterExpressionType Type => FilterExpressionType.Logical;

	internal override bool IsTrue(in FilterContext context)
	{
		foreach (var operand in Operands)
		{
			if (operand.IsTrue(context))
			{
				return true;
			}
		}

		return false;
	}

	internal override void AppendTo(StringBuilder builder)
	{
		for (var i = 0; i < Operands.Length; i++)
		{
			if (i > 0)
			{
				builder.Append(" || ");
			}

			AppendOperand(builder, Operands[i], parenthesize: Operands[i] is OrExpression);
		}
	}

	/// <summary>
	/// Compares the operands item by item.
	/// </summary>
	public bool Equals(OrExpression? other) => other is not null && Guard.ItemsEqual(Operands, other.Operands);

	/// <inheritdoc/>
	public override int GetHashCode() => Guard.GetItemsHashCode(Operands);
}

/// <summary>
/// Logical AND, like <c>@.a &amp;&amp; @.b</c>: true if all of the operands are true; evaluated from left to right, stopping at the first false operand.
/// </summary>
/// <param name="Operands">The logical expressions, in the order they are written.</param>
public sealed record class AndExpression(ImmutableArray<FilterExpression> Operands) : FilterExpression
{
	/// <summary>
	/// The logical expressions; there is at least one.
	/// </summary>
	public ImmutableArray<FilterExpression> Operands { get; init => field = OrExpression.CheckOperands(value); } = OrExpression.CheckOperands(Operands);

	/// <summary>
	/// Returns the operands of an AND expression, or the expression itself otherwise, so <c>a &amp; b &amp; c</c> makes a single expression.
	/// </summary>
	internal static ImmutableArray<FilterExpression> Flatten(FilterExpression expression) => expression is AndExpression conjunction ? conjunction.Operands : [expression];

	/// <inheritdoc/>
	public override FilterExpressionType Type => FilterExpressionType.Logical;

	internal override bool IsTrue(in FilterContext context)
	{
		foreach (var operand in Operands)
		{
			if (!operand.IsTrue(context))
			{
				return false;
			}
		}

		return true;
	}

	internal override void AppendTo(StringBuilder builder)
	{
		for (var i = 0; i < Operands.Length; i++)
		{
			if (i > 0)
			{
				builder.Append(" && ");
			}

			AppendOperand(builder, Operands[i], parenthesize: Operands[i] is OrExpression or AndExpression);
		}
	}

	/// <summary>
	/// Compares the operands item by item.
	/// </summary>
	public bool Equals(AndExpression? other) => other is not null && Guard.ItemsEqual(Operands, other.Operands);

	/// <inheritdoc/>
	public override int GetHashCode() => Guard.GetItemsHashCode(Operands);
}

/// <summary>
/// Logical NOT, like <c>!@.a</c> or <c>!(@.a == 1)</c>: true if the operand is false.
/// </summary>
/// <param name="Operand">The logical expression to negate.</param>
public sealed record class NotExpression(FilterExpression Operand) : FilterExpression
{
	/// <summary>
	/// The logical expression to negate.
	/// </summary>
	public FilterExpression Operand { get; init => field = CheckLogical(value); } = CheckLogical(Operand);

	/// <inheritdoc/>
	public override FilterExpressionType Type => FilterExpressionType.Logical;

	internal override bool IsTrue(in FilterContext context) => !Operand.IsTrue(context);

	internal override void AppendTo(StringBuilder builder)
	{
		builder.Append('!');

		// only a query or a function call may follow the operator without parentheses
		AppendOperand(builder, Operand, parenthesize: Operand is not (QueryExpression or FunctionExpression));
	}
}
