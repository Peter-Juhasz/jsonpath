using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Evaluation;

/// <summary>
/// The kinds of <see cref="FilterValue"/>.
/// </summary>
internal enum FilterValueKind
{
	/// <summary>
	/// Nothing: the absence of a value, produced by a singular query that selects no node, or a function without a result.
	/// </summary>
	Nothing,

	/// <summary>
	/// A JSON value in a document or a literal.
	/// </summary>
	Element,

	/// <summary>
	/// An integer computed by a function, like <c>length()</c>, which is kept as it is rather than as a JSON value, so computing it does not allocate.
	/// </summary>
	Integer,
}

/// <summary>
/// A value of <c>ValueType</c> in a filter: a JSON value, or Nothing.
/// </summary>
internal readonly struct FilterValue
{
	public FilterValue(JsonElement element)
	{
		Kind = FilterValueKind.Element;
		Element = element;
	}

	public FilterValue(long integer)
	{
		Kind = FilterValueKind.Integer;
		Integer = integer;
	}

	public FilterValueKind Kind { get; }

	/// <summary>
	/// The value, if <see cref="Kind"/> is <see cref="FilterValueKind.Element"/>.
	/// </summary>
	public JsonElement Element { get; }

	/// <summary>
	/// The value, if <see cref="Kind"/> is <see cref="FilterValueKind.Integer"/>.
	/// </summary>
	public long Integer { get; }

	public bool IsNothing => Kind == FilterValueKind.Nothing;

	public bool IsNumber => Kind == FilterValueKind.Integer || (Kind == FilterValueKind.Element && Element.ValueKind == JsonValueKind.Number);

	public bool IsString => Kind == FilterValueKind.Element && Element.ValueKind == JsonValueKind.String;
}
