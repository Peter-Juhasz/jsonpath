using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// The types of the expressions in filters, as defined by RFC 9535 (section 2.4.1), which decide where an expression may appear.
/// </summary>
public enum FilterExpressionType
{
	/// <summary>
	/// <c>ValueType</c>: a JSON value, or Nothing; produced by literals, singular queries, and functions like <c>length()</c>. Values can be compared.
	/// </summary>
	Value,

	/// <summary>
	/// <c>LogicalType</c>: true or false; produced by comparisons, logical operators, and functions like <c>match()</c>.
	/// </summary>
	Logical,

	/// <summary>
	/// <c>NodesType</c>: a list of nodes; produced by queries. Where a logical value is expected, nodes are true if there is any.
	/// </summary>
	Nodes,
}

/// <summary>
/// An expression in a filter: a logical expression made of comparisons and tests combined with <c>&amp;&amp;</c>, <c>||</c> and <c>!</c>,
/// or one of the operands they are made of, which are literals, queries and function calls.
/// </summary>
public closed record class FilterExpression
{
	/// <summary>
	/// The type of the expression, which decides where it may appear.
	/// </summary>
	public abstract FilterExpressionType Type { get; }

	/// <summary>
	/// Checks whether the expression can be used where an expression of the given type is expected, like an argument of a function:
	/// a value must be a literal, a singular query or a function returning a value; a logical value may also be nodes, which are true if there is any.
	/// </summary>
	public bool IsConvertibleTo(FilterExpressionType type) => type switch
	{
		FilterExpressionType.Value => IsComparable,
		FilterExpressionType.Logical => Type is not FilterExpressionType.Value,
		FilterExpressionType.Nodes => Type is FilterExpressionType.Nodes,
		_ => false,
	};

	/// <summary>
	/// Whether the expression can be an operand of a comparison: a literal, a singular query or a function returning a value.
	/// </summary>
	internal bool IsComparable => this switch
	{
		LiteralExpression => true,
		QueryExpression query => query.Query.IsSingular,
		FunctionExpression function => function.Type is FilterExpressionType.Value,
		_ => false,
	};

	internal static FilterExpression CheckLogical(FilterExpression expression, string? paramName = "value")
	{
		ArgumentNullException.ThrowIfNull(expression, paramName);
		return expression.IsConvertibleTo(FilterExpressionType.Logical) ? expression : throw new ArgumentException($"'{expression}' is not a logical expression.", paramName);
	}

	/// <summary>
	/// Evaluates an expression of <see cref="FilterExpressionType.Logical"/> type, or tests whether one of <see cref="FilterExpressionType.Nodes"/> type selects any node.
	/// </summary>
	internal virtual bool IsTrue(in FilterContext context) => throw new InvalidOperationException($"'{this}' does not produce a logical value.");

	/// <summary>
	/// Evaluates an expression that can be compared, see <see cref="IsComparable"/>.
	/// </summary>
	internal virtual FilterValue Evaluate(in FilterContext context) => throw new InvalidOperationException($"'{this}' does not produce a value.");

	/// <summary>
	/// Counts the nodes of an expression of <see cref="FilterExpressionType.Nodes"/> type.
	/// </summary>
	internal virtual int CountNodes(in FilterContext context) => throw new InvalidOperationException($"'{this}' does not produce nodes.");

	/// <summary>
	/// Gets the value of the node of an expression of <see cref="FilterExpressionType.Nodes"/> type, if it produces exactly one.
	/// </summary>
	internal virtual bool TryGetSingleNode(in FilterContext context, out JsonElement value) => throw new InvalidOperationException($"'{this}' does not produce nodes.");

	/// <summary>
	/// Combines two logical expressions with <c>&amp;&amp;</c>; <c>a &amp; b &amp; c</c> makes a single expression with three operands.
	/// </summary>
	public static AndExpression operator &(FilterExpression left, FilterExpression right) => new([.. AndExpression.Flatten(left), .. AndExpression.Flatten(right)]);

	/// <summary>
	/// Combines two logical expressions with <c>||</c>; <c>a | b | c</c> makes a single expression with three operands.
	/// </summary>
	public static OrExpression operator |(FilterExpression left, FilterExpression right) => new([.. OrExpression.Flatten(left), .. OrExpression.Flatten(right)]);

	/// <summary>
	/// Negates a logical expression with <c>!</c>.
	/// </summary>
	public static NotExpression operator !(FilterExpression operand) => new(operand);

	/// <summary>
	/// Returns the expression as written in a filter, with parentheses only where they are needed.
	/// </summary>
	public sealed override string ToString()
	{
		using var pooled = StringBuilderPool.GetPooledObject(out var builder);
		AppendTo(builder);
		return builder.ToString();
	}

	internal abstract void AppendTo(StringBuilder builder);

	/// <summary>
	/// Writes an operand, in parentheses if <paramref name="parenthesize"/> is true.
	/// </summary>
	private protected static void AppendOperand(StringBuilder builder, FilterExpression operand, bool parenthesize)
	{
		if (parenthesize)
		{
			builder.Append('(');
			operand.AppendTo(builder);
			builder.Append(')');
		}
		else
		{
			operand.AppendTo(builder);
		}
	}
}

/// <summary>
/// The state that filter expressions are evaluated in.
/// </summary>
/// <param name="Root">The root node of the document, which absolute queries, <c>$</c>, start from.</param>
/// <param name="Current">The node that the filter checks, which relative queries, <c>@</c>, start from.</param>
internal readonly record struct FilterContext(JsonElement Root, JsonElement Current);
