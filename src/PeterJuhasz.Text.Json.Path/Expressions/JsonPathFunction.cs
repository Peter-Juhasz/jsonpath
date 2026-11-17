using PeterJuhasz.Text.Json.Path.Evaluation;
using PeterJuhasz.Text.Json.Path.Functions;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// A function that can be called in filters, with its signature, which decides where calls to it may appear and what arguments they may have.
/// The functions are those defined by RFC 9535 (section 2.4): <c>length()</c>, <c>count()</c>, <c>match()</c>, <c>search()</c> and <c>value()</c>.
/// </summary>
public abstract class JsonPathFunction
{
	private protected JsonPathFunction(string name, FilterExpressionType resultType, ImmutableArray<FilterExpressionType> parameterTypes)
	{
		Name = name;
		ResultType = resultType;
		ParameterTypes = parameterTypes;
	}

	/// <summary>
	/// The name the function is called by.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// The type of the result of the function.
	/// </summary>
	public FilterExpressionType ResultType { get; }

	/// <summary>
	/// The types of the parameters of the function, in order.
	/// </summary>
	public ImmutableArray<FilterExpressionType> ParameterTypes { get; }

	/// <summary>
	/// <c>length(value)</c>: the number of Unicode scalar values of a string, elements of an array or members of an object; Nothing for other values.
	/// </summary>
	public static JsonPathFunction Length { get; } = new LengthFunction();

	/// <summary>
	/// <c>count(nodes)</c>: the number of nodes.
	/// </summary>
	public static JsonPathFunction Count { get; } = new CountFunction();

	/// <summary>
	/// <c>match(string, pattern)</c>: whether the whole string matches the I-Regexp (RFC 9485) pattern; false if either is not a string, or the pattern is not valid.
	/// </summary>
	public static JsonPathFunction Match { get; } = new RegexFunction("match", anchored: true);

	/// <summary>
	/// <c>search(string, pattern)</c>: whether a substring of the string matches the I-Regexp (RFC 9485) pattern; false if either is not a string, or the pattern is not valid.
	/// </summary>
	public static JsonPathFunction Search { get; } = new RegexFunction("search", anchored: false);

	/// <summary>
	/// <c>value(nodes)</c>: the value of the node if there is exactly one, Nothing otherwise.
	/// </summary>
	public static JsonPathFunction Value { get; } = new ValueFunction();

	private static readonly FrozenDictionary<string, JsonPathFunction> Functions = new[] { Length, Count, Match, Search, Value }.ToFrozenDictionary(f => f.Name, StringComparer.Ordinal);

	private static readonly FrozenDictionary<string, JsonPathFunction>.AlternateLookup<ReadOnlySpan<char>> FunctionsBySpan = Functions.GetAlternateLookup<ReadOnlySpan<char>>();

	/// <summary>
	/// Looks up a function by its name, which is case-sensitive.
	/// </summary>
	public static bool TryGet(ReadOnlySpan<char> name, [NotNullWhen(true)] out JsonPathFunction? function) => FunctionsBySpan.TryGetValue(name, out function);

	/// <summary>
	/// Calls a function of <see cref="FilterExpressionType.Value"/> result type.
	/// </summary>
	internal virtual FilterValue Evaluate(FunctionExpression call, in FilterContext context) => throw new InvalidOperationException($"'{Name}' does not return a value.");

	/// <summary>
	/// Calls a function of <see cref="FilterExpressionType.Logical"/> result type.
	/// </summary>
	internal virtual bool IsTrue(FunctionExpression call, in FilterContext context) => throw new InvalidOperationException($"'{Name}' does not return a logical value.");

	/// <summary>
	/// Calls a function of <see cref="FilterExpressionType.Nodes"/> result type and counts the nodes it returns.
	/// </summary>
	internal virtual int CountNodes(FunctionExpression call, in FilterContext context) => throw new InvalidOperationException($"'{Name}' does not return nodes.");

	/// <summary>
	/// Calls a function of <see cref="FilterExpressionType.Nodes"/> result type and gets the node it returns, if it returns exactly one.
	/// </summary>
	internal virtual bool TryGetSingleNode(FunctionExpression call, in FilterContext context, out JsonElement value) => throw new InvalidOperationException($"'{Name}' does not return nodes.");

	/// <summary>
	/// Returns the name of the function.
	/// </summary>
	public override string ToString() => Name;
}
