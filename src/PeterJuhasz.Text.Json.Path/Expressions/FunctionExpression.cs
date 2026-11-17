using PeterJuhasz.Text.Json.Path.Evaluation;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PeterJuhasz.Text.Json.Path.Expressions;

/// <summary>
/// A function call, like <c>length(@.name)</c> or <c>match(@.date, '1974-05-..')</c>; its type is the result type of the function.
/// </summary>
/// <param name="Function">The function.</param>
/// <param name="Arguments">The arguments, one for each parameter of the function, each convertible to the type of the parameter.</param>
public sealed record class FunctionExpression(JsonPathFunction Function, ImmutableArray<FilterExpression> Arguments) : FilterExpression
{
	/// <summary>
	/// The function.
	/// </summary>
	public JsonPathFunction Function
	{
		get;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			field = value;
			if (!Arguments.IsDefault)
			{
				CheckArguments(value, Arguments);
			}
		}
	} = Function ?? throw new ArgumentNullException(nameof(Function));

	/// <summary>
	/// The arguments, one for each parameter of the function.
	/// </summary>
	public ImmutableArray<FilterExpression> Arguments { get; init => field = CheckArguments(Function, value); } = CheckArguments(Function, Arguments);

	private static ImmutableArray<FilterExpression> CheckArguments(JsonPathFunction function, ImmutableArray<FilterExpression> arguments)
	{
		Guard.NotDefault(arguments);
		var parameters = function.ParameterTypes;
		if (arguments.Length != parameters.Length)
		{
			throw new ArgumentException($"'{function.Name}' takes {parameters.Length} argument(s), not {arguments.Length}.", nameof(arguments));
		}

		for (var i = 0; i < arguments.Length; i++)
		{
			ArgumentNullException.ThrowIfNull(arguments[i], nameof(arguments));
			if (!arguments[i].IsConvertibleTo(parameters[i]))
			{
				throw new ArgumentException($"Argument {i + 1} of '{function.Name}', '{arguments[i]}', is not of {parameters[i]} type.", nameof(arguments));
			}
		}

		return arguments;
	}

	/// <summary>
	/// The regular expression compiled from the pattern argument of <c>match()</c> and <c>search()</c> when it is a literal, so it is compiled only once.
	/// </summary>
	private CompiledPattern? _compiledPattern;

	/// <inheritdoc/>
	public override FilterExpressionType Type => Function.ResultType;

	internal override bool IsTrue(in FilterContext context) => Function.ResultType switch
	{
		FilterExpressionType.Logical => Function.IsTrue(this, context),
		FilterExpressionType.Nodes => Function.CountNodes(this, context) > 0,
		_ => base.IsTrue(context),
	};

	internal override FilterValue Evaluate(in FilterContext context) => Function.ResultType is FilterExpressionType.Value ? Function.Evaluate(this, context) : base.Evaluate(context);

	internal override int CountNodes(in FilterContext context) => Function.ResultType is FilterExpressionType.Nodes ? Function.CountNodes(this, context) : base.CountNodes(context);

	internal override bool TryGetSingleNode(in FilterContext context, out JsonElement value) => Function.ResultType is FilterExpressionType.Nodes
		? Function.TryGetSingleNode(this, context, out value)
		: base.TryGetSingleNode(context, out value);

	/// <summary>
	/// Gets the regular expression for a literal pattern argument, compiling it the first time; null if the pattern is not a valid I-Regexp.
	/// </summary>
	internal Regex? GetCompiledPattern(LiteralExpression pattern, Func<string, Regex?> compile)
	{
		// the pattern is checked by reference, as 'with' copies the compiled pattern along with the other fields
		if (_compiledPattern is { } compiled && ReferenceEquals(compiled.Pattern, pattern))
		{
			return compiled.Regex;
		}

		var regex = compile(pattern.Value.GetString()!);
		_compiledPattern = new CompiledPattern(pattern, regex);
		return regex;
	}

	private sealed record class CompiledPattern(LiteralExpression Pattern, Regex? Regex);

	internal override void AppendTo(StringBuilder builder)
	{
		builder.Append(Function.Name).Append('(');
		for (var i = 0; i < Arguments.Length; i++)
		{
			if (i > 0)
			{
				builder.Append(", ");
			}

			Arguments[i].AppendTo(builder);
		}

		builder.Append(')');
	}

	/// <summary>
	/// Compares the functions and the arguments item by item.
	/// </summary>
	public bool Equals(FunctionExpression? other) => other is not null && Function == other.Function && Guard.ItemsEqual(Arguments, other.Arguments);

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(Function, Guard.GetItemsHashCode(Arguments));
}
