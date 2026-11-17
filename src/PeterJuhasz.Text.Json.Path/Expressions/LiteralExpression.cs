using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// A literal, like <c>'text'</c>, <c>1.5</c>, <c>true</c>, <c>false</c> or <c>null</c>: a JSON value written in the query.
/// </summary>
/// <param name="Value">The value: a string, a number, true, false or null.</param>
public sealed record class LiteralExpression(JsonElement Value) : FilterExpression
{
	/// <summary>
	/// The value: a string, a number, true, false or null.
	/// </summary>
	public JsonElement Value { get; init => field = CheckPrimitive(value); } = CheckPrimitive(Value);

	private static JsonElement CheckPrimitive(JsonElement value) => value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null
		? value
		: throw new ArgumentException($"A literal must be a string, a number, true, false or null, not {value.ValueKind}.", nameof(value));

	/// <summary>
	/// The literal <c>true</c>.
	/// </summary>
	public static LiteralExpression True { get; } = new(JsonElement.Parse("true"));

	/// <summary>
	/// The literal <c>false</c>.
	/// </summary>
	public static LiteralExpression False { get; } = new(JsonElement.Parse("false"));

	/// <summary>
	/// The literal <c>null</c>.
	/// </summary>
	public static LiteralExpression Null { get; } = new(JsonElement.Parse("null"));

	/// <summary>
	/// Creates a string literal.
	/// </summary>
	public static LiteralExpression String(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		var buffer = new ArrayBufferWriter<byte>(value.Length + 2);
		using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
		{
			writer.WriteStringValue(value);
		}

		return new(JsonElement.Parse(buffer.WrittenSpan));
	}

	/// <summary>
	/// Creates a number literal.
	/// </summary>
	public static LiteralExpression Number(long value) => new(JsonElement.Parse(value.ToString(CultureInfo.InvariantCulture)));

	/// <summary>
	/// Creates a number literal.
	/// </summary>
	public static LiteralExpression Number(double value) => double.IsFinite(value)
		? new(JsonElement.Parse(value.ToString("R", CultureInfo.InvariantCulture)))
		: throw new ArgumentOutOfRangeException(nameof(value), value, "JSON numbers must be finite.");

	/// <inheritdoc/>
	public override FilterExpressionType Type => FilterExpressionType.Value;

	internal override FilterValue Evaluate(in FilterContext context) => new(Value);

	internal override void AppendTo(StringBuilder builder)
	{
		if (Value.ValueKind == JsonValueKind.String)
		{
			SyntaxFacts.AppendStringLiteral(builder, Value.GetString()!);
		}
		else
		{
			builder.Append(Value.GetRawText());
		}
	}

	/// <summary>
	/// Compares the values as a comparison in a filter would, so numbers are equal by their value, like <c>1</c> and <c>1.0</c>.
	/// </summary>
	public bool Equals(LiteralExpression? other) => other is not null && FilterValueComparer.AreEqual(Value, other.Value);

	/// <inheritdoc/>
	public override int GetHashCode() => Value.ValueKind switch
	{
		JsonValueKind.String => StringComparer.Ordinal.GetHashCode(Value.GetString()!),
		JsonValueKind.Number => FilterValueComparer.GetDouble(Value).GetHashCode(),
		var kind => kind.GetHashCode(),
	};
}
