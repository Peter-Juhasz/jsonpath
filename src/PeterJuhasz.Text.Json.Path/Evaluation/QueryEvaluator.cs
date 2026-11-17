using PeterJuhasz.Text.Json.Path.Expressions;
using System.Collections.Immutable;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Evaluation;

/// <summary>
/// What the evaluation of a query keeps of the nodes it selects.
/// </summary>
internal enum EvaluationMode
{
	/// <summary>
	/// Only the count and the first node, for tests, <c>count()</c> and <c>value()</c>.
	/// </summary>
	None,

	/// <summary>
	/// The values of the nodes.
	/// </summary>
	Elements,

	/// <summary>
	/// The values of the nodes and their locations.
	/// </summary>
	Nodes,
}

/// <summary>
/// Evaluates the segments of a query depth first: each node a segment selects is passed on to the next segment right away, and the ones that pass all of them are kept.
/// This yields the nodelist in the order RFC 9535 defines, as concatenating the results for each node in order is the same as visiting them in order,
/// without building a list for each segment; and it lets the evaluation stop as soon as it has found as many nodes as it needs.
/// </summary>
internal ref struct QueryEvaluator
{
	private readonly ReadOnlySpan<JsonPathSegment> _segments;

	private readonly EvaluationMode _mode;

	private readonly int _limit;

	private List<JsonElement>? _elements;

	private List<JsonPathNode>? _nodes;

	/// <summary>
	/// Creates an evaluator for a query.
	/// </summary>
	/// <param name="segments">The segments of the query.</param>
	/// <param name="root">The root node, which absolute queries in filters start from.</param>
	/// <param name="mode">What to keep of the selected nodes; locations are only tracked for <see cref="EvaluationMode.Nodes"/>.</param>
	/// <param name="limit">How many nodes to find before stopping.</param>
	public QueryEvaluator(ImmutableArray<JsonPathSegment> segments, JsonElement root, EvaluationMode mode, int limit = int.MaxValue)
	{
		_segments = segments.AsSpan();
		Root = root;
		_mode = mode;
		_limit = limit;
	}

	/// <summary>
	/// The root node, which absolute queries in filters start from.
	/// </summary>
	public readonly JsonElement Root { get; }

	/// <summary>
	/// The number of nodes selected so far.
	/// </summary>
	public int Count { get; private set; }

	/// <summary>
	/// The value of the first node selected.
	/// </summary>
	public JsonElement First { get; private set; }

	/// <summary>
	/// The values of the selected nodes, in <see cref="EvaluationMode.Elements"/> mode.
	/// </summary>
	public readonly IReadOnlyList<JsonElement> Elements => _elements ?? (IReadOnlyList<JsonElement>)[];

	/// <summary>
	/// The selected nodes, in <see cref="EvaluationMode.Nodes"/> mode.
	/// </summary>
	public readonly IReadOnlyList<JsonPathNode> Nodes => _nodes ?? (IReadOnlyList<JsonPathNode>)[];

	/// <summary>
	/// Passes a node to the segment at <paramref name="index"/>, or keeps it if it has passed all of them.
	/// </summary>
	/// <param name="index">The index of the segment to apply next.</param>
	/// <param name="value">The node.</param>
	/// <param name="location">The location of the node; null unless locations are tracked.</param>
	/// <returns>False if the evaluation should stop, as it has found as many nodes as it needs.</returns>
	public bool Visit(int index, JsonElement value, NormalizedPath? location)
	{
		if (index < _segments.Length)
		{
			return _segments[index].Select(ref this, index + 1, value, location);
		}

		if (Count == 0)
		{
			First = value;
		}

		Count++;
		switch (_mode)
		{
			case EvaluationMode.Elements:
				(_elements ??= []).Add(value);
				break;

			case EvaluationMode.Nodes:
				(_nodes ??= []).Add(new JsonPathNode(value, location!));
				break;
		}

		return Count < _limit;
	}
}
