using static PeterJuhasz.Text.Json.Path.Tests.TestHelpers;

namespace PeterJuhasz.Text.Json.Path.Tests;

[TestClass]
public sealed class ParserTests
{
	private static QueryExpression Current(string name) => new(JsonPathQuery.Current.Child(name));

	[TestMethod]
	public void ParsesSegmentsAndSelectorsIntoATree()
	{
		var query = JsonPathQuery.Parse("$.a['b', 1][1:-1:2]..*[?@.c]");

		var expected = new JsonPathQuery([
			new ChildSegment([new NameSelector("a")]),
			new ChildSegment([new NameSelector("b"), new IndexSelector(1)]),
			new ChildSegment([new SliceSelector(1, -1, 2)]),
			new DescendantSegment([WildcardSelector.Instance]),
			new ChildSegment([new FilterSelector(Current("c"))]),
		]);
		Assert.AreEqual(expected, query);
	}

	[TestMethod]
	public void ParsesLogicalOperatorsByPrecedence()
	{
		var query = JsonPathQuery.Parse("$[?@.a == 1 || @.b && !(@.c < 'x')]");

		var expected = new OrExpression([
			new ComparisonExpression(Current("a"), ComparisonOperator.Equal, LiteralExpression.Number(1)),
			new AndExpression([
				Current("b"),
				new NotExpression(new ComparisonExpression(Current("c"), ComparisonOperator.LessThan, LiteralExpression.String("x"))),
			]),
		]);
		Assert.AreEqual(expected, ((FilterSelector)query.Segments[0].Selectors[0]).Expression);
	}

	[TestMethod]
	public void ParsesFunctionCallsWithTheirArguments()
	{
		var query = JsonPathQuery.Parse("$[?match(@.a, 'x.*') && count($..b) > length(@.c)]");

		var expected = new AndExpression([
			new FunctionExpression(JsonPathFunction.Match, [Current("a"), LiteralExpression.String("x.*")]),
			new ComparisonExpression(
				new FunctionExpression(JsonPathFunction.Count, [new QueryExpression(JsonPathQuery.Root.Descendant(new NameSelector("b")))]),
				ComparisonOperator.GreaterThan,
				new FunctionExpression(JsonPathFunction.Length, [Current("c")])),
		]);
		Assert.AreEqual(expected, ((FilterSelector)query.Segments[0].Selectors[0]).Expression);
	}

	[TestMethod]
	[DataRow("$", "$")]
	[DataRow("$.a", "$.a")]
	[DataRow("$['a']", "$.a")]
	[DataRow("$[\"a b\"]", "$['a b']")]
	[DataRow("$['it\\'s']", "$['it\\'s']")]
	[DataRow("$[\"say \\\"hi\\\"\"]", "$['say \"hi\"']")]
	[DataRow("$['\\u00e9\\n']", "$['é\\n']")]
	[DataRow("$['1a']", "$['1a']")]
	[DataRow("$[ 'a' , 0 ]", "$['a', 0]")]
	[DataRow("$[*]", "$.*")]
	[DataRow("$..['a']", "$..a")]
	[DataRow("$..[0, 'a']", "$..[0, 'a']")]
	[DataRow("$[1 : 2]", "$[1:2]")]
	[DataRow("$[::]", "$[:]")]
	[DataRow("$[::-1]", "$[::-1]")]
	[DataRow("$[?@.a==1]", "$[?@.a == 1]")]
	[DataRow("$[?(@.a)]", "$[?@.a]")]
	[DataRow("$[?!(@.a)]", "$[?!@.a]")]
	[DataRow("$[?!(@.a == 1)]", "$[?!(@.a == 1)]")]
	[DataRow("$[?!(!@.a)]", "$[?!(!@.a)]")]
	[DataRow("$[?(@.a || @.b) && @.c]", "$[?(@.a || @.b) && @.c]")]
	[DataRow("$[?@.a || (@.b || @.c)]", "$[?@.a || (@.b || @.c)]")]
	[DataRow("$[?@.a&&@.b||@.c]", "$[?@.a && @.b || @.c]")]
	[DataRow("$[?length(@.a)>=2]", "$[?length(@.a) >= 2]")]
	[DataRow("$[?@.a == \"x\"]", "$[?@.a == 'x']")]
	[DataRow("$[?@.a == 1.50e+3]", "$[?@.a == 1.50e+3]")]
	[DataRow("$[?@.a == -0]", "$[?@.a == -0]")]
	[DataRow("$[?@['b c'][0] != $.d]", "$[?@['b c'][0] != $.d]")]
	[DataRow("$[?@.a == null && @.b != true && @.c != false]", "$[?@.a == null && @.b != true && @.c != false]")]
	public void WritesQueriesInCanonicalForm(string query, string expected)
	{
		var parsed = JsonPathQuery.Parse(query);

		Assert.AreEqual(expected, parsed.ToString());
		Assert.AreEqual(parsed, JsonPathQuery.Parse(expected));
	}

	[TestMethod]
	[DataRow(" $", 0, "expected '$'")]
	[DataRow("$.a ", 3, "unexpected ' '")]
	[DataRow("$.1", 2, "expected a member name or '*' after '.'")]
	[DataRow("$[01]", 2, "leading zeros")]
	[DataRow("$[-0]", 2, "'-0' is not a valid integer")]
	[DataRow("$[9007199254740992]", 2, "integers must be between")]
	[DataRow("$['a]", 2, "not closed")]
	[DataRow("$[\"\\'\"]", 3, "is not a valid escape")]
	[DataRow("$['\\uDC00']", 3, "a low surrogate must follow a high surrogate")]
	[DataRow("$[?@.* == 1]", 3, "only singular queries can be compared")]
	[DataRow("$[?1]", 3, "a literal must be compared")]
	[DataRow("$[?length(@.a)]", 3, "the result of 'length()' must be compared")]
	[DataRow("$[?match(@.a, 'x') == true]", 3, "the result of 'match()' is not a value")]
	[DataRow("$[?count(1) == 1]", 9, "argument 1 of 'count()' must be a query")]
	[DataRow("$[?length((@.a)) == 1]", 10, "argument 1 of 'length()' must be a value")]
	[DataRow("$[?length(@.a, @.b) == 1]", 15, "'length()' takes 1 argument(s)")]
	[DataRow("$[?foo(@.a)]", 3, "unknown function 'foo'")]
	[DataRow("$[?@.a == truex]", 10, "unexpected 'truex'")]
	[DataRow("$[?!1]", 4, "'!' must be followed by")]
	public void ReportsWhyAndWhereAQueryIsInvalid(string query, int position, string reason)
	{
		Assert.IsFalse(JsonPathQuery.TryParse(query, out _, out var error));

		Assert.Contains(reason, error);
		Assert.EndsWith($"at position {position}.", error);
	}

	[TestMethod]
	public void LimitsHowDeeplyExpressionsAreNested()
	{
		static string Nested(int depth) => $"$[?{new string('(', depth)}@.a{new string(')', depth)}]";

		Assert.IsTrue(JsonPathQuery.TryParse(Nested(JsonPathParser.MaxDepth - 1), out _));
		Assert.IsFalse(JsonPathQuery.TryParse(Nested(100_000), out _, out var error));
		Assert.Contains("nesting is deeper than", error);
	}

	[TestMethod]
	public void AcceptsTheLimitsOfTheIntegerRange()
	{
		Assert.AreEqual(new IndexSelector(SyntaxFacts.MaxSafeInteger), JsonPathQuery.Parse("$[9007199254740991]").Segments[0].Selectors[0]);
		Assert.AreEqual(new IndexSelector(SyntaxFacts.MinSafeInteger), JsonPathQuery.Parse("$[-9007199254740991]").Segments[0].Selectors[0]);
		AssertInvalid("$[-9007199254740992]");
		AssertInvalid("$[1:9007199254740992]");
	}

	[TestMethod]
	public void ParsesSpans()
	{
		Assert.IsTrue(JsonPathQuery.TryParse("$.a.b".AsSpan(0, 3), out var query));
		Assert.AreEqual("$.a", query.ToString());
	}
}
