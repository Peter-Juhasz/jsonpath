using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// A query in a filter, relative to the current node like <c>@.price</c>, or absolute like <c>$.limit</c>. On its own, it tests whether it selects any node;
/// compared, it must be singular and produces the value of the node it selects, or Nothing if it selects none.
/// </summary>
/// <param name="Query">The query.</param>
public sealed record class QueryExpression(JsonPathQuery Query) : FilterExpression
{
	/// <summary>
	/// The query.
	/// </summary>
	public JsonPathQuery Query
	{
		get;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			field = value;
			_isSingular = value.IsSingular;
		}
	} = Query ?? throw new ArgumentNullException(nameof(Query));

	private bool _isSingular = Query.IsSingular;

	/// <inheritdoc/>
	public override FilterExpressionType Type => FilterExpressionType.Nodes;

	private JsonElement GetStart(in FilterContext context) => Query.IsRelative ? context.Current : context.Root;

	internal override bool IsTrue(in FilterContext context)
	{
		var evaluator = new QueryEvaluator(Query.Segments, context.Root, EvaluationMode.None, limit: 1);
		evaluator.Visit(0, GetStart(context), location: null);
		return evaluator.Count > 0;
	}

	internal override FilterValue Evaluate(in FilterContext context)
	{
		if (!_isSingular)
		{
			return TryGetSingleNode(context, out var value) ? new(value) : default;
		}

		// a singular query is a path of names and indices, which is followed without evaluating it as a query
		var current = GetStart(context);
		foreach (var segment in Query.Segments)
		{
			switch (segment.Selectors[0])
			{
				case NameSelector name:
					if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name.Utf8Name, out current))
					{
						return default;
					}

					break;

				case IndexSelector selector:
					if (current.ValueKind != JsonValueKind.Array)
					{
						return default;
					}

					var length = current.GetArrayLength();
					var index = selector.Index >= 0 ? selector.Index : length + selector.Index;
					if (index < 0 || index >= length)
					{
						return default;
					}

					current = current[(int)index];
					break;
			}
		}

		return new(current);
	}

	internal override int CountNodes(in FilterContext context)
	{
		var evaluator = new QueryEvaluator(Query.Segments, context.Root, EvaluationMode.None);
		evaluator.Visit(0, GetStart(context), location: null);
		return evaluator.Count;
	}

	internal override bool TryGetSingleNode(in FilterContext context, out JsonElement value)
	{
		var evaluator = new QueryEvaluator(Query.Segments, context.Root, EvaluationMode.None, limit: 2);
		evaluator.Visit(0, GetStart(context), location: null);
		value = evaluator.First;
		return evaluator.Count == 1;
	}

	internal override void AppendTo(StringBuilder builder) => Query.AppendTo(builder);

	/// <summary>
	/// Compares the queries.
	/// </summary>
	public bool Equals(QueryExpression? other) => other is not null && Query.Equals(other.Query);

	/// <inheritdoc/>
	public override int GetHashCode() => Query.GetHashCode();
}
