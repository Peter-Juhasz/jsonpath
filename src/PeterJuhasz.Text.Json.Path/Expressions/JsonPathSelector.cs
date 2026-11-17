using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// A selector of a segment: selects children of a node, like a member of an object by name or an element of an array by index.
/// Selectors that do not apply to the kind of the node, like an index selector to an object, select nothing.
/// </summary>
public closed record class JsonPathSelector
{
	/// <summary>
	/// Selects the children of a node, passing each to <paramref name="evaluator"/> to continue with the segment at <paramref name="next"/>.
	/// </summary>
	/// <returns>False if the evaluation should stop, as it has found as many nodes as it needed.</returns>
	internal abstract bool Select(ref QueryEvaluator evaluator, int next, JsonElement value, NormalizedPath? location);

	/// <summary>
	/// Returns the selector as written in brackets in a query.
	/// </summary>
	public sealed override string ToString()
	{
		using var pooled = StringBuilderPool.GetPooledObject(out var builder);
		AppendTo(builder);
		return builder.ToString();
	}

	internal abstract void AppendTo(StringBuilder builder);
}
