using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Text;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// A name selector, like <c>'name'</c> or the shorthand <c>.name</c>: selects the member of an object with the name, if it has one.
/// </summary>
/// <param name="Name">The member name, compared with the names in the document as sequences of Unicode scalar values, after unescaping.</param>
public sealed record class NameSelector(string Name) : JsonPathSelector
{
	/// <summary>
	/// The member name.
	/// </summary>
	public string Name
	{
		get;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			field = value;
			Utf8Name = Encoding.UTF8.GetBytes(value);
		}
	} = Name ?? throw new ArgumentNullException(nameof(Name));

	/// <summary>
	/// The name encoded as UTF-8, so the member can be looked up without transcoding the names in the document.
	/// </summary>
	internal byte[] Utf8Name { get; private set; } = Encoding.UTF8.GetBytes(Name);

	internal override bool Select(ref QueryEvaluator evaluator, int next, JsonElement value, NormalizedPath? location)
	{
		if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(Utf8Name, out var member))
		{
			return evaluator.Visit(next, member, location?.Append(Name));
		}

		return true;
	}

	internal override void AppendTo(StringBuilder builder) => SyntaxFacts.AppendStringLiteral(builder, Name);

	/// <summary>
	/// Compares the names ordinally.
	/// </summary>
	public bool Equals(NameSelector? other) => other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

	/// <inheritdoc/>
	public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);
}
