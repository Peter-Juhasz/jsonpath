using PeterJuhasz.Text.Json.Path.Evaluation;
using PeterJuhasz.Text.Json.Path.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PeterJuhasz.Text.Json.Path.Functions;

/// <summary>
/// <c>length(value)</c> (RFC 9535, section 2.4.4).
/// </summary>
internal sealed class LengthFunction() : JsonPathFunction("length", FilterExpressionType.Value, [FilterExpressionType.Value])
{
	internal override FilterValue Evaluate(FunctionExpression call, in FilterContext context)
	{
		var argument = call.Arguments[0].Evaluate(context);
		if (argument.Kind != FilterValueKind.Element)
		{
			return default;
		}

		var value = argument.Element;
		return value.ValueKind switch
		{
			JsonValueKind.String => new(FilterValueComparer.CountScalarValues(value)),
			JsonValueKind.Array => new((long)value.GetArrayLength()),
			JsonValueKind.Object => new((long)value.GetPropertyCount()),
			_ => default,
		};
	}
}

/// <summary>
/// <c>count(nodes)</c> (RFC 9535, section 2.4.5).
/// </summary>
internal sealed class CountFunction() : JsonPathFunction("count", FilterExpressionType.Value, [FilterExpressionType.Nodes])
{
	internal override FilterValue Evaluate(FunctionExpression call, in FilterContext context) => new((long)call.Arguments[0].CountNodes(context));
}

/// <summary>
/// <c>value(nodes)</c> (RFC 9535, section 2.4.8).
/// </summary>
internal sealed class ValueFunction() : JsonPathFunction("value", FilterExpressionType.Value, [FilterExpressionType.Nodes])
{
	internal override FilterValue Evaluate(FunctionExpression call, in FilterContext context) => call.Arguments[0].TryGetSingleNode(context, out var value) ? new(value) : default;
}

/// <summary>
/// <c>match(string, pattern)</c> and <c>search(string, pattern)</c> (RFC 9535, sections 2.4.6 and 2.4.7), which differ only in whether the whole string must match.
/// </summary>
internal sealed class RegexFunction(string name, bool anchored) : JsonPathFunction(name, FilterExpressionType.Logical, [FilterExpressionType.Value, FilterExpressionType.Value])
{
	private readonly Func<string, Regex?> _compile = anchored ? IRegexp.GetAnchored : IRegexp.GetUnanchored;

	internal override bool IsTrue(FunctionExpression call, in FilterContext context)
	{
		var input = call.Arguments[0].Evaluate(context);
		if (!input.IsString)
		{
			return false;
		}

		Regex? regex;
		if (call.Arguments[1] is LiteralExpression literal)
		{
			if (literal.Value.ValueKind != JsonValueKind.String)
			{
				return false;
			}

			regex = call.GetCompiledPattern(literal, _compile);
		}
		else
		{
			var pattern = call.Arguments[1].Evaluate(context);
			if (!pattern.IsString)
			{
				return false;
			}

			regex = _compile(pattern.Element.GetString()!);
		}

		return regex is not null && regex.IsMatch(input.Element.GetString()!);
	}
}
