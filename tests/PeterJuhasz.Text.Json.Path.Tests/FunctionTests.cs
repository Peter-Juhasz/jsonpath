using PeterJuhasz.Text.Json.Path.Functions;
using System.Collections.Immutable;
using static PeterJuhasz.Text.Json.Path.Tests.TestHelpers;

namespace PeterJuhasz.Text.Json.Path.Tests;

[TestClass]
public sealed class FunctionTests
{
	[TestMethod]
	public void CountsUnicodeScalarValuesObjectMembersAndArrayElements()
		=> Assert.AreEqual("""["é","é","😀","😀",[1],{"a":1}]""", Select("""["é","\u00e9","😀","\ud83d\ude00",[1],{"a":1},1,"ab",true]""", "$[?length(@) == 1]"));

	[TestMethod]
	public void CountsNodes() => Assert.AreEqual("""[[1,2],{"a":1,"b":2}]""", Select("""[[1,2],{"a":1,"b":2},[1],[[1,2]]]""", "$[?count(@.*) == 2]"));

	[TestMethod]
	public void ReturnsTheValueOfASingleNode() => Assert.AreEqual("""[{"a":{"x":1}}]""", Select("""[{"a":{"x":1}},{"a":{"x":1},"b":{"x":1}},{"a":{"x":2}}]""", "$[?value(@..x) == 1]"));

	private const string Patterns = """{"p":"a\\.c","items":[{"s":"a.c","p":"x"},{"s":"abc"},{"s":"xa.c"}]}""";

	[TestMethod]
	[DataRow("$.items[?match(@.s, $.p)].s", """["a.c"]""")]
	[DataRow("$.items[?search(@.s, $.p)].s", """["a.c","xa.c"]""")]
	[DataRow("$.items[?match(@.s, @.p)].s", "[]")]
	[DataRow("$.items[?match(@.s, 1)].s", "[]")]
	[DataRow("$.items[?match(1, @.s)].s", "[]")]
	public void MatchesPatternsFromTheDocument(string query, string expected) => Assert.AreEqual(expected, Select(Patterns, query));

	[TestMethod]
	public void RejectsLoneSurrogates()
	{
		Assert.IsNull(IRegexp.Create("\uD800", anchored: true));
		Assert.IsNull(IRegexp.Create("[\uDC00]", anchored: true));
	}

	[TestMethod]
	[DataRow("\\d")]
	[DataRow("\\w")]
	[DataRow("\\s")]
	[DataRow("\\u0041")]
	[DataRow("(?:a)")]
	[DataRow("(?=a)")]
	[DataRow("a*?")]
	[DataRow("a**")]
	[DataRow("a{2,1}")]
	[DataRow("a{")]
	[DataRow("a{1")]
	[DataRow("a{,1}")]
	[DataRow("[]")]
	[DataRow("[^]")]
	[DataRow("[z-a]")]
	[DataRow("[a-c-e]")]
	[DataRow("[a")]
	[DataRow("[[]")]
	[DataRow("\\p{Xx}")]
	[DataRow("\\p{IsBasicLatin}")]
	[DataRow("\\p{L")]
	[DataRow("(a")]
	[DataRow("a)")]
	[DataRow("\\")]
	[DataRow("\\$")]
	public void RejectsPatternsThatAreNotIRegexp(string pattern) => Assert.IsNull(IRegexp.Create(pattern, anchored: true), IRegexp.TryTranslate(pattern, out var translated) ? translated : null);

	[TestMethod]
	[DataRow(".", "😀", true)]
	[DataRow("..", "😀", false)]
	[DataRow(".", "\n", false)]
	[DataRow(".", "\r", false)]
	[DataRow("[^a]", "😀", true)]
	[DataRow("[^a][^a]", "😀", false)]
	[DataRow("[😀-😂]", "😁", true)]
	[DataRow("[😀-😂]", "😃", false)]
	[DataRow("[^😀-😂]", "😃", true)]
	[DataRow("[^😀-😂]", "😁", false)]
	[DataRow("[a-😀]", "z", true)]
	[DataRow("[a-😀]", "\uFFFD", true)]
	[DataRow("[a-😀]", "😀", true)]
	[DataRow("[a-😀]", "😁", false)]
	[DataRow("[🌀-🗿]+", "🌀🍕🗿", true)]
	[DataRow("[🌀-🗿]", "😀", false)]
	[DataRow("😀+", "😀😀", true)]
	[DataRow("\\p{L}", "é", true)]
	[DataRow("\\p{Lu}", "é", false)]
	[DataRow("\\P{L}", "1", true)]
	[DataRow("\\P{L}", "😀", true)]
	[DataRow("\\P{L}\\P{L}", "😀", false)]
	[DataRow("[\\p{Nd}x]+", "12x3", true)]
	[DataRow("[^\\p{Nd}]", "1", false)]
	[DataRow("\\p{C}", "\u0001", true)]
	[DataRow("\\p{C}\\p{C}", "😀", false)]
	[DataRow("a{2}", "aa", true)]
	[DataRow("a{2,}", "aaa", true)]
	[DataRow("a{2,3}", "aaaa", false)]
	[DataRow("[.]", ".", true)]
	[DataRow("[.]", "a", false)]
	[DataRow("\\.", "a", false)]
	[DataRow("[-a]+", "-a", true)]
	[DataRow("[a-]+", "-a", true)]
	[DataRow("[--]", "-", true)]
	[DataRow("[\\-\\]\\[]+", "-][", true)]
	[DataRow("[a^]+", "^a", true)]
	[DataRow("\\^[$]", "^$", true)]
	[DataRow("^ab$", "ab", true)]
	[DataRow("a|b|", "", true)]
	[DataRow("()", "", true)]
	[DataRow("\\t\\n\\r", "\t\n\r", true)]
	[DataRow("#", "#", true)]
	[DataRow("a b", "a b", true)]
	public void MatchesCodePoints(string pattern, string input, bool matches)
	{
		var regex = IRegexp.Create(pattern, anchored: true);

		Assert.IsNotNull(regex, pattern);
		Assert.AreEqual(matches, regex.IsMatch(input), $"'{pattern}' was translated to '{regex}'.");
	}

	[TestMethod]
	public void SearchesSubstrings()
	{
		Assert.IsTrue(IRegexp.Create("b.", anchored: false)!.IsMatch("abc"));
		Assert.IsFalse(IRegexp.Create("b.", anchored: true)!.IsMatch("abc"));
		Assert.IsFalse(IRegexp.Create("^b", anchored: false)!.IsMatch("abc"));
	}

	[TestMethod]
	public void CachesCompiledPatterns() => Assert.AreSame(IRegexp.GetAnchored("cached+"), IRegexp.GetAnchored("cached+"));

	[TestMethod]
	public void CompilesLiteralPatternsAgainWhenTheArgumentIsReplaced()
	{
		var match = new FunctionExpression(JsonPathFunction.Match, [new QueryExpression(JsonPathQuery.Current), LiteralExpression.String("a")]);
		var query = JsonPathQuery.Root.Child(new FilterSelector(match));
		var json = ParseJson("""["a","b"]""");
		Assert.AreEqual("""["a"]""", ToJson(query.SelectElements(json)));

		var replaced = JsonPathQuery.Root.Child(new FilterSelector(match with { Arguments = match.Arguments.SetItem(1, LiteralExpression.String("b")) }));
		Assert.AreEqual("""["b"]""", ToJson(replaced.SelectElements(json)));
		Assert.AreEqual("""["a"]""", ToJson(query.SelectElements(json)));
	}

	[TestMethod]
	public void HasTheSignaturesOfTheRfc()
	{
		Assert.IsTrue(JsonPathFunction.TryGet("length", out var length));
		Assert.AreSame(JsonPathFunction.Length, length);
		Assert.AreEqual(FilterExpressionType.Value, length.ResultType);
		Assert.AreSequenceEqual(ImmutableArray.Create(FilterExpressionType.Value, FilterExpressionType.Value), JsonPathFunction.Match.ParameterTypes);
		Assert.AreEqual(FilterExpressionType.Logical, JsonPathFunction.Search.ResultType);
		Assert.IsFalse(JsonPathFunction.TryGet("Length", out _));
	}
}
