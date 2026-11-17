using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path;

/// <summary>
/// A node selected by a query: a value in the JSON document and its location.
/// </summary>
/// <param name="Value">The value of the node.</param>
/// <param name="Location">The location of the node in the document, as a normalized path like <c>$['store']['book'][0]</c>.</param>
public readonly record struct JsonPathNode(JsonElement Value, NormalizedPath Location);
