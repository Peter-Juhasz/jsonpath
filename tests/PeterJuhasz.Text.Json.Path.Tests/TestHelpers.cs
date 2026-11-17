namespace PeterJuhasz.Text.Json.Path.Tests;

/// <summary>
/// Helpers shared by the tests.
/// </summary>
internal static class TestHelpers
{
	/// <summary>
	/// Parses a JSON document; independent of any document that would need to be disposed.
	/// </summary>
	public static JsonElement ParseJson(string json) => JsonElement.Parse(json);

	/// <summary>
	/// Writes the elements as a compact JSON array, to compare results in a readable way: strings are written decoded, numbers as they are in the document.
	/// </summary>
	public static string ToJson(IEnumerable<JsonElement> elements)
	{
		var buffer = new System.Buffers.ArrayBufferWriter<byte>();
		using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
		{
			writer.WriteStartArray();
			foreach (var element in elements)
			{
				Write(writer, element);
			}

			writer.WriteEndArray();
		}

		return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	private static void Write(Utf8JsonWriter writer, JsonElement element)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach (var item in element.EnumerateArray())
				{
					Write(writer, item);
				}

				writer.WriteEndArray();
				break;

			case JsonValueKind.Object:
				writer.WriteStartObject();
				foreach (var property in element.EnumerateObject())
				{
					writer.WritePropertyName(property.Name);
					Write(writer, property.Value);
				}

				writer.WriteEndObject();
				break;

			// strings are written as they are, as even the relaxed encoder escapes characters outside the Basic Multilingual Plane
			case JsonValueKind.String:
				writer.WriteRawValue($"\"{element.GetString()!.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"", skipInputValidation: true);
				break;

			default:
				writer.WriteRawValue(element.GetRawText());
				break;
		}
	}

	/// <summary>
	/// Evaluates a query and returns the selected values as a compact JSON array.
	/// </summary>
	public static string Select(string json, string query) => ToJson(JsonPathQuery.Parse(query).SelectElements(ParseJson(json)));

	/// <summary>
	/// Evaluates a query and returns the locations of the selected nodes as normalized paths.
	/// </summary>
	public static string[] Paths(string json, string query) => [.. JsonPathQuery.Parse(query).SelectNodes(ParseJson(json)).Select(n => n.Location.ToString())];

	/// <summary>
	/// Asserts that the query is not valid.
	/// </summary>
	public static void AssertInvalid(string query)
	{
		Assert.IsFalse(JsonPathQuery.TryParse(query, out var result, out var error), $"'{query}' was parsed as '{result}'.");
		Assert.IsNotNull(error);
		Assert.ThrowsExactly<FormatException>(() => JsonPathQuery.Parse(query));
	}
}
