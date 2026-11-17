using PeterJuhasz.Text.Json.Path.Evaluation;
using PeterJuhasz.Text.Json.Path.Expressions;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path;

/// <summary>
/// A parsed JSONPath query as defined by RFC 9535: an identifier, <c>$</c> for the root node or <c>@</c> for the current node in a filter, followed by segments,
/// each of which selects nodes from the nodes selected by the previous one. The query is immutable and thread-safe, so it can be parsed once and evaluated many times.
/// </summary>
/// <param name="Segments">The segments, applied in order; a query without segments selects the node it starts from.</param>
/// <param name="IsRelative">Whether the query starts with <c>@</c>, the current node of a filter, rather than <c>$</c>, the root node; such queries can only appear in filters.</param>
public sealed record class JsonPathQuery(ImmutableArray<JsonPathSegment> Segments, bool IsRelative = false)
{
	/// <summary>
	/// The query <c>$</c>, which selects the root node; append segments to build other queries.
	/// </summary>
	public static JsonPathQuery Root { get; } = new([]);

	/// <summary>
	/// The query <c>@</c>, which selects the current node of a filter; append segments to build other relative queries.
	/// </summary>
	public static JsonPathQuery Current { get; } = new([], IsRelative: true);

	/// <summary>
	/// The segments, applied in order.
	/// </summary>
	public ImmutableArray<JsonPathSegment> Segments { get; init => field = Guard.NotDefault(value); } = Guard.NotDefault(Segments);

	/// <summary>
	/// Whether the query selects at most one node: each segment is a child segment with a single name or index selector, like <c>$.a[0]</c>.
	/// Only singular queries can be compared in filters.
	/// </summary>
	public bool IsSingular
	{
		get
		{
			foreach (var segment in Segments)
			{
				if (segment is not ChildSegment { Selectors: [NameSelector or IndexSelector] })
				{
					return false;
				}
			}

			return true;
		}
	}

	/// <summary>
	/// Parses a query written in the syntax of RFC 9535.
	/// </summary>
	/// <exception cref="FormatException">The query is not valid; the message tells why and where.</exception>
	public static JsonPathQuery Parse(string query)
	{
		ArgumentNullException.ThrowIfNull(query);
		return TryParse(query, out var result, out var error) ? result : throw new FormatException(error);
	}

	/// <summary>
	/// Parses a query written in the syntax of RFC 9535, returning false if it is not valid.
	/// </summary>
	public static bool TryParse(ReadOnlySpan<char> query, [NotNullWhen(true)] out JsonPathQuery? result) => JsonPathParser.TryParse(query, out result, out _);

	/// <summary>
	/// Parses a query written in the syntax of RFC 9535, returning false with a message that tells why and where, if it is not valid.
	/// </summary>
	public static bool TryParse(ReadOnlySpan<char> query, [NotNullWhen(true)] out JsonPathQuery? result, [NotNullWhen(false)] out string? error) => JsonPathParser.TryParse(query, out result, out error);

	/// <summary>
	/// Returns a query with a segment appended.
	/// </summary>
	public JsonPathQuery Append(JsonPathSegment segment)
	{
		ArgumentNullException.ThrowIfNull(segment);
		return this with { Segments = Segments.Add(segment) };
	}

	/// <summary>
	/// Returns a query with a child segment of a single name selector appended, like <c>.name</c>.
	/// </summary>
	public JsonPathQuery Child(string name) => Append(new ChildSegment([new NameSelector(name)]));

	/// <summary>
	/// Returns a query with a child segment of a single index selector appended, like <c>[0]</c>.
	/// </summary>
	public JsonPathQuery Child(long index) => Append(new ChildSegment([new IndexSelector(index)]));

	/// <summary>
	/// Returns a query with a child segment of the given selectors appended, like <c>['a', 'b']</c>.
	/// </summary>
	public JsonPathQuery Child(params ReadOnlySpan<JsonPathSelector> selectors) => Append(new ChildSegment([.. selectors]));

	/// <summary>
	/// Returns a query with a descendant segment of the given selectors appended, like <c>..['a', 'b']</c>.
	/// </summary>
	public JsonPathQuery Descendant(params ReadOnlySpan<JsonPathSelector> selectors) => Append(new DescendantSegment([.. selectors]));

	/// <summary>
	/// Selects the values of the nodes that the query selects from <paramref name="root"/>, in the order of the nodelist defined by RFC 9535.
	/// </summary>
	public IReadOnlyList<JsonElement> SelectElements(JsonElement root)
	{
		var evaluator = new QueryEvaluator(Segments, root, EvaluationMode.Elements);
		evaluator.Visit(0, root, location: null);
		return evaluator.Elements;
	}

	/// <summary>
	/// Selects the nodes that the query selects from <paramref name="root"/>, in the order of the nodelist defined by RFC 9535, with their locations as normalized paths.
	/// Prefer <see cref="SelectElements(JsonElement)"/> when the locations are not needed, as building them allocates.
	/// </summary>
	public IReadOnlyList<JsonPathNode> SelectNodes(JsonElement root)
	{
		var evaluator = new QueryEvaluator(Segments, root, EvaluationMode.Nodes);
		evaluator.Visit(0, root, NormalizedPath.Root);
		return evaluator.Nodes;
	}

	/// <summary>
	/// Selects the value of the first node that the query selects from <paramref name="root"/>, stopping the evaluation as soon as it is found.
	/// </summary>
	/// <returns>Whether the query selects any node.</returns>
	public bool TrySelectFirst(JsonElement root, out JsonElement value)
	{
		var evaluator = new QueryEvaluator(Segments, root, EvaluationMode.None, limit: 1);
		evaluator.Visit(0, root, location: null);
		value = evaluator.First;
		return evaluator.Count > 0;
	}

	/// <summary>
	/// Returns the query in the syntax of RFC 9535, which parses back to an equal query. Names are written in shorthand form where possible, like <c>$.a['b c']</c>.
	/// </summary>
	public override string ToString()
	{
		using var pooled = StringBuilderPool.GetPooledObject(out var builder);
		AppendTo(builder);
		return builder.ToString();
	}

	internal void AppendTo(StringBuilder builder)
	{
		builder.Append(IsRelative ? '@' : '$');
		foreach (var segment in Segments)
		{
			segment.AppendTo(builder);
		}
	}

	/// <summary>
	/// Compares the identifier and the segments item by item.
	/// </summary>
	public bool Equals(JsonPathQuery? other) => other is not null && IsRelative == other.IsRelative && Guard.ItemsEqual(Segments, other.Segments);

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(IsRelative, Guard.GetItemsHashCode(Segments));
}
