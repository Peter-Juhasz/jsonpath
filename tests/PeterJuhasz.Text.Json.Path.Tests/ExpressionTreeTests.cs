using static PeterJuhasz.Text.Json.Path.Tests.TestHelpers;

namespace PeterJuhasz.Text.Json.Path.Tests;

[TestClass]
public sealed class ExpressionTreeTests
{
	private static readonly QueryExpression Price = new(JsonPathQuery.Current.Child("price"));

	[TestMethod]
	public void BuildsQueries()
	{
		var query = JsonPathQuery.Root
			.Child("store")
			.Descendant(new NameSelector("book"))
			.Child(new SliceSelector(Step: -1), new IndexSelector(0))
			.Child(new FilterSelector(new ComparisonExpression(Price, ComparisonOperator.LessThan, LiteralExpression.Number(10)) & !new QueryExpression(JsonPathQuery.Current.Child("isbn"))));

		Assert.AreEqual("$.store..book[::-1, 0][?@.price < 10 && !@.isbn]", query.ToString());
		Assert.AreEqual(JsonPathQuery.Parse(query.ToString()), query);
	}

	[TestMethod]
	public void CombinesLogicalExpressionsWithOperators()
	{
		var a = new QueryExpression(JsonPathQuery.Current.Child("a"));
		var b = new QueryExpression(JsonPathQuery.Current.Child("b"));
		var c = new QueryExpression(JsonPathQuery.Current.Child("c"));

		Assert.AreEqual(new AndExpression([a, b, c]), a & b & c);
		Assert.AreEqual(new OrExpression([a, b, c]), a | (b | c));
		Assert.AreEqual("@.a || @.b && @.c", (a | (b & c)).ToString());
		Assert.AreEqual("(@.a || @.b) && @.c", ((a | b) & c).ToString());
		Assert.AreEqual("!(@.a && @.b)", (!(a & b)).ToString());
	}

	[TestMethod]
	public void RejectsTreesThatAreNotWellTyped()
	{
		var nonSingular = new QueryExpression(JsonPathQuery.Current.Child(WildcardSelector.Instance));
		var length = new FunctionExpression(JsonPathFunction.Length, [Price]);

		Assert.ThrowsExactly<ArgumentException>(() => new ComparisonExpression(nonSingular, ComparisonOperator.Equal, LiteralExpression.Null));
		Assert.ThrowsExactly<ArgumentException>(() => new ComparisonExpression(Price & Price, ComparisonOperator.Equal, LiteralExpression.Null));
		Assert.ThrowsExactly<ArgumentException>(() => new FilterSelector(LiteralExpression.True));
		Assert.ThrowsExactly<ArgumentException>(() => new FilterSelector(length));
		Assert.ThrowsExactly<ArgumentException>(() => new NotExpression(length));
		Assert.ThrowsExactly<ArgumentException>(() => new AndExpression([Price, LiteralExpression.True]));
		Assert.ThrowsExactly<ArgumentException>(() => new FunctionExpression(JsonPathFunction.Count, [LiteralExpression.Number(1)]));
		Assert.ThrowsExactly<ArgumentException>(() => new FunctionExpression(JsonPathFunction.Length, [nonSingular]));
		Assert.ThrowsExactly<ArgumentException>(() => new FunctionExpression(JsonPathFunction.Length, []));
		Assert.ThrowsExactly<ArgumentException>(() => length with { Function = JsonPathFunction.Match });
		Assert.ThrowsExactly<ArgumentException>(() => new ChildSegment([]));
		Assert.ThrowsExactly<ArgumentException>(() => new JsonPathQuery(default(System.Collections.Immutable.ImmutableArray<JsonPathSegment>)));
		Assert.ThrowsExactly<ArgumentException>(() => new LiteralExpression(ParseJson("[]")));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new IndexSelector(SyntaxFacts.MaxSafeInteger + 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SliceSelector(End: SyntaxFacts.MinSafeInteger - 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LiteralExpression.Number(double.NaN));
	}

	[TestMethod]
	public void ConvertsExpressionsToTheTypesOfParameters()
	{
		var nodes = new QueryExpression(JsonPathQuery.Current.Child(WildcardSelector.Instance));
		var match = new FunctionExpression(JsonPathFunction.Match, [Price, LiteralExpression.String("1.*")]);

		Assert.IsTrue(Price.IsConvertibleTo(FilterExpressionType.Value));
		Assert.IsFalse(nodes.IsConvertibleTo(FilterExpressionType.Value));
		Assert.IsTrue(nodes.IsConvertibleTo(FilterExpressionType.Logical));
		Assert.IsTrue(nodes.IsConvertibleTo(FilterExpressionType.Nodes));
		Assert.IsTrue(match.IsConvertibleTo(FilterExpressionType.Logical));
		Assert.IsFalse(match.IsConvertibleTo(FilterExpressionType.Value));
		Assert.IsFalse(LiteralExpression.True.IsConvertibleTo(FilterExpressionType.Logical));
	}

	[TestMethod]
	public void KeepsDerivedStateConsistentWhenCopied()
	{
		var json = ParseJson("""{"a":1,"b":2,"items":[{"x":[1]},{"x":[1,2]}]}""");
		var name = new NameSelector("a");
		var copy = name with { Name = "b" };
		Assert.AreEqual("[2]", ToJson(JsonPathQuery.Root.Child(copy).SelectElements(json)));

		// a query that is no longer singular must not be followed as a path
		var first = new QueryExpression(JsonPathQuery.Current.Child("x").Child(0));
		var all = first with { Query = JsonPathQuery.Current.Child("x").Child(WildcardSelector.Instance) };
		var count = new FunctionExpression(JsonPathFunction.Value, [all]);
		Assert.AreEqual("""[{"x":[1]}]""", ToJson(JsonPathQuery.Parse("$.items").Child(new FilterSelector(new ComparisonExpression(count, ComparisonOperator.Equal, LiteralExpression.Number(1)))).SelectElements(json)));
	}

	[TestMethod]
	public void ComparesLiteralsByValue()
	{
		var one = (LiteralExpression)((ComparisonExpression)((FilterSelector)JsonPathQuery.Parse("$[?@ == 1.0]").Segments[0].Selectors[0]).Expression).Right;

		Assert.AreEqual(LiteralExpression.Number(1), one);
		Assert.AreEqual(LiteralExpression.Number(1).GetHashCode(), one.GetHashCode());
		Assert.AreNotEqual(LiteralExpression.String("1"), one);
		Assert.AreEqual(LiteralExpression.String("a'b"), LiteralExpression.String("a'b"));
		Assert.AreEqual("'a\\'b'", LiteralExpression.String("a'b").ToString());
	}

	[TestMethod]
	public void EvaluatesRelativeQueriesFromTheGivenNode()
		=> Assert.AreEqual("[1]", ToJson(JsonPathQuery.Current.Child("a").SelectElements(ParseJson("""{"a":1}"""))));
}
