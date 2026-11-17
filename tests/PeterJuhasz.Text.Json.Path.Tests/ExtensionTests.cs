using static PeterJuhasz.Text.Json.Path.Tests.TestHelpers;

namespace PeterJuhasz.Text.Json.Path.Tests;

[TestClass]
public sealed class ExtensionTests
{
	private const string Json = """{"a":[{"b":1},{"b":2}]}""";

	[TestMethod]
	public void SelectsFromDocuments()
	{
		using var document = JsonDocument.Parse(Json);

		Assert.AreEqual("[1,2]", ToJson(document.SelectElements("$..b")));
		Assert.AreEqual("[2]", ToJson(document.SelectElements(JsonPathQuery.Parse("$.a[-1].b"))));
		Assert.AreSequenceEqual(new[] { "$['a'][0]['b']", "$['a'][1]['b']" }, document.SelectNodes("$..b").Select(node => node.Location.ToString()).ToArray());
		Assert.HasCount(2, document.SelectNodes(JsonPathQuery.Parse("$.a.*")));
		Assert.IsTrue(document.TrySelectFirst("$..b", out var first));
		Assert.AreEqual(1, first.GetInt32());
		Assert.IsFalse(document.TrySelectFirst(JsonPathQuery.Parse("$.c"), out _));
	}

	[TestMethod]
	public void SelectsFromElements()
	{
		var element = TestHelpers.ParseJson(Json).GetProperty("a");

		Assert.AreEqual("[1,2]", ToJson(element.SelectElements("$[*].b")));
		Assert.AreEqual("$[1]['b']", element.SelectNodes("$[1].b").Single().Location.ToString());
		Assert.IsTrue(element.TrySelectFirst("$[?@.b > 1]", out var match));
		Assert.AreEqual("""{"b":2}""", match.GetRawText());
	}

	[TestMethod]
	public void RejectsInvalidQueries()
	{
		var element = TestHelpers.ParseJson(Json);

		Assert.ThrowsExactly<FormatException>(() => element.SelectElements("$["));
		Assert.ThrowsExactly<ArgumentNullException>(() => element.SelectElements((string)null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => ((JsonDocument)null!).SelectElements("$"));
	}

	[TestMethod]
	public void ReusesParsedQueries() => Assert.AreSame(QueryCache.Parse("$.cached"), QueryCache.Parse("$.cached"));
}
