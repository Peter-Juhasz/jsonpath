using System.Collections.Concurrent;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path;

/// <summary>
/// Evaluates JSONPath queries on <see cref="JsonElement"/> and <see cref="JsonDocument"/>.
/// The overloads that take the query as a string cache the recently parsed queries; parse a <see cref="JsonPathQuery"/> once to control it.
/// </summary>
public static class JsonPathExtensions
{
	extension(JsonElement element)
	{
		/// <summary>
		/// Selects the values of the nodes that the query selects from this element as the root, in the order of the nodelist defined by RFC 9535.
		/// </summary>
		/// <exception cref="FormatException">The query is not valid.</exception>
		public IReadOnlyList<JsonElement> SelectElements(string jsonPath) => QueryCache.Parse(jsonPath).SelectElements(element);

		/// <summary>
		/// Selects the values of the nodes that the query selects from this element as the root, in the order of the nodelist defined by RFC 9535.
		/// </summary>
		public IReadOnlyList<JsonElement> SelectElements(JsonPathQuery query)
		{
			ArgumentNullException.ThrowIfNull(query);
			return query.SelectElements(element);
		}

		/// <summary>
		/// Selects the nodes that the query selects from this element as the root, with their locations as normalized paths.
		/// </summary>
		/// <exception cref="FormatException">The query is not valid.</exception>
		public IReadOnlyList<JsonPathNode> SelectNodes(string jsonPath) => QueryCache.Parse(jsonPath).SelectNodes(element);

		/// <summary>
		/// Selects the nodes that the query selects from this element as the root, with their locations as normalized paths.
		/// </summary>
		public IReadOnlyList<JsonPathNode> SelectNodes(JsonPathQuery query)
		{
			ArgumentNullException.ThrowIfNull(query);
			return query.SelectNodes(element);
		}

		/// <summary>
		/// Selects the value of the first node that the query selects from this element as the root, stopping as soon as it is found.
		/// </summary>
		/// <exception cref="FormatException">The query is not valid.</exception>
		public bool TrySelectFirst(string jsonPath, out JsonElement value) => QueryCache.Parse(jsonPath).TrySelectFirst(element, out value);

		/// <summary>
		/// Selects the value of the first node that the query selects from this element as the root, stopping as soon as it is found.
		/// </summary>
		public bool TrySelectFirst(JsonPathQuery query, out JsonElement value)
		{
			ArgumentNullException.ThrowIfNull(query);
			return query.TrySelectFirst(element, out value);
		}
	}

	extension(JsonDocument document)
	{
		/// <summary>
		/// Selects the values of the nodes that the query selects from the root of the document, in the order of the nodelist defined by RFC 9535.
		/// </summary>
		/// <exception cref="FormatException">The query is not valid.</exception>
		public IReadOnlyList<JsonElement> SelectElements(string jsonPath)
		{
			ArgumentNullException.ThrowIfNull(document);
			return document.RootElement.SelectElements(jsonPath);
		}

		/// <summary>
		/// Selects the values of the nodes that the query selects from the root of the document, in the order of the nodelist defined by RFC 9535.
		/// </summary>
		public IReadOnlyList<JsonElement> SelectElements(JsonPathQuery query)
		{
			ArgumentNullException.ThrowIfNull(document);
			return document.RootElement.SelectElements(query);
		}

		/// <summary>
		/// Selects the nodes that the query selects from the root of the document, with their locations as normalized paths.
		/// </summary>
		/// <exception cref="FormatException">The query is not valid.</exception>
		public IReadOnlyList<JsonPathNode> SelectNodes(string jsonPath)
		{
			ArgumentNullException.ThrowIfNull(document);
			return document.RootElement.SelectNodes(jsonPath);
		}

		/// <summary>
		/// Selects the nodes that the query selects from the root of the document, with their locations as normalized paths.
		/// </summary>
		public IReadOnlyList<JsonPathNode> SelectNodes(JsonPathQuery query)
		{
			ArgumentNullException.ThrowIfNull(document);
			return document.RootElement.SelectNodes(query);
		}

		/// <summary>
		/// Selects the value of the first node that the query selects from the root of the document, stopping as soon as it is found.
		/// </summary>
		/// <exception cref="FormatException">The query is not valid.</exception>
		public bool TrySelectFirst(string jsonPath, out JsonElement value)
		{
			ArgumentNullException.ThrowIfNull(document);
			return document.RootElement.TrySelectFirst(jsonPath, out value);
		}

		/// <summary>
		/// Selects the value of the first node that the query selects from the root of the document, stopping as soon as it is found.
		/// </summary>
		public bool TrySelectFirst(JsonPathQuery query, out JsonElement value)
		{
			ArgumentNullException.ThrowIfNull(document);
			return document.RootElement.TrySelectFirst(query, out value);
		}
	}
}

/// <summary>
/// The recently parsed queries of the overloads that take the query as a string, so that evaluating the same query in a loop parses it only once.
/// </summary>
internal static class QueryCache
{
	/// <summary>
	/// How many queries are kept; when the cache is full, it is cleared, which is cheaper than tracking which queries were used recently.
	/// </summary>
	internal const int Capacity = 256;

	private static readonly ConcurrentDictionary<string, JsonPathQuery> Queries = new(StringComparer.Ordinal);

	public static JsonPathQuery Parse(string jsonPath)
	{
		ArgumentNullException.ThrowIfNull(jsonPath);
		if (Queries.TryGetValue(jsonPath, out var query))
		{
			return query;
		}

		query = JsonPathQuery.Parse(jsonPath);
		if (Queries.Count >= Capacity)
		{
			Queries.Clear();
		}

		Queries.TryAdd(jsonPath, query);
		return query;
	}
}
