using static PeterJuhasz.Text.Json.Path.Tests.TestHelpers;

namespace PeterJuhasz.Text.Json.Path.Tests;

[TestClass]
public sealed class EvaluationTests
{
	/// <summary>
	/// The example of RFC 9535 (section 1.5).
	/// </summary>
	private const string Store = """
		{ "store": {
		    "book": [
		      { "category": "reference", "author": "Nigel Rees", "title": "Sayings of the Century", "price": 8.95 },
		      { "category": "fiction", "author": "Evelyn Waugh", "title": "Sword of Honour", "price": 12.99 },
		      { "category": "fiction", "author": "Herman Melville", "title": "Moby Dick", "isbn": "0-553-21311-3", "price": 8.99 },
		      { "category": "fiction", "author": "J. R. R. Tolkien", "title": "The Lord of the Rings", "isbn": "0-395-19395-8", "price": 22.99 }
		    ],
		    "bicycle": { "color": "red", "price": 399 }
		  }
		}
		""";

	[TestMethod]
	[DataRow("$.store.book[*].author", """["Nigel Rees","Evelyn Waugh","Herman Melville","J. R. R. Tolkien"]""")]
	[DataRow("$..author", """["Nigel Rees","Evelyn Waugh","Herman Melville","J. R. R. Tolkien"]""")]
	[DataRow("$.store..price", "[8.95,12.99,8.99,22.99,399]")]
	[DataRow("$..book[2].author", """["Herman Melville"]""")]
	[DataRow("$..book[2].publisher", "[]")]
	[DataRow("$..book[-1].title", """["The Lord of the Rings"]""")]
	[DataRow("$..book[0,1].title", """["Sayings of the Century","Sword of Honour"]""")]
	[DataRow("$..book[:2].title", """["Sayings of the Century","Sword of Honour"]""")]
	[DataRow("$..book[?@.isbn].title", """["Moby Dick","The Lord of the Rings"]""")]
	[DataRow("$..book[?@.price<10].title", """["Sayings of the Century","Moby Dick"]""")]
	public void EvaluatesTheExamplesOfTheRfc(string query, string expected) => Assert.AreEqual(expected, Select(Store, query));

	[TestMethod]
	public void LocatesTheNodesWithNormalizedPaths()
	{
		Assert.AreSequenceEqual(
			new[] { "$['store']['book'][0]['author']", "$['store']['book'][1]['author']", "$['store']['book'][2]['author']", "$['store']['book'][3]['author']" },
			Paths(Store, "$..author"));
		Assert.AreSequenceEqual(new[] { "$['store']['book'][3]" }, Paths(Store, "$.store.book[-1]"));
	}

	[TestMethod]
	public void VisitsDescendantsBeforeTheirSiblings()
	{
		const string json = """{"a":[1,{"b":2}],"c":3}""";

		Assert.AreEqual("""[[1,{"b":2}],3,1,{"b":2},2]""", Select(json, "$..*"));
		Assert.AreSequenceEqual(new[] { "$['a']", "$['c']", "$['a'][0]", "$['a'][1]", "$['a'][1]['b']" }, Paths(json, "$..*"));
	}

	[TestMethod]
	[DataRow("$[1:5:2]", "[1,3]")]
	[DataRow("$[5:1:-2]", "[5,3]")]
	[DataRow("$[::-1]", "[9,8,7,6,5,4,3,2,1,0]")]
	[DataRow("$[-2:]", "[8,9]")]
	[DataRow("$[:-8]", "[0,1]")]
	[DataRow("$[-100:100:3]", "[0,3,6,9]")]
	[DataRow("$[100:-100:-4]", "[9,5,1]")]
	[DataRow("$[::0]", "[]")]
	[DataRow("$[3:3]", "[]")]
	[DataRow("$[-9007199254740991:9007199254740991:9007199254740991]", "[0]")]
	[DataRow("$[9007199254740991::-9007199254740991]", "[9]")]
	public void SlicesArrays(string query, string expected) => Assert.AreEqual(expected, Select("[0,1,2,3,4,5,6,7,8,9]", query));

	[TestMethod]
	public void LocatesSlicedElementsByTheirIndices() => Assert.AreSequenceEqual(new[] { "$[4]", "$[2]", "$[0]" }, Paths("[0,1,2,3,4]", "$[::-2]"));

	[TestMethod]
	[DataRow("$.a", "[0,1]")]
	[DataRow("$[0]", """{"a":1}""")]
	[DataRow("$[0:1]", """{"a":1}""")]
	[DataRow("$[?@]", "1")]
	[DataRow("$.*", "1")]
	public void SelectsNothingFromValuesOfOtherKinds(string query, string json) => Assert.AreEqual("[]", Select(json, query));

	[TestMethod]
	public void KeepsDuplicates() => Assert.AreEqual("[1,1,1]", Select("[1,2]", "$[0, 0, -2]"));

	[TestMethod]
	public void SelectsTheFirstNodeOnly()
	{
		var query = JsonPathQuery.Parse("$..[?@ > 1]");

		Assert.IsTrue(query.TrySelectFirst(ParseJson("[1,[3,2],4]"), out var first));
		// arrays are not ordered against numbers, so the first match is the last element of the root, before the descendants
		Assert.AreEqual("4", first.GetRawText());
		Assert.IsFalse(query.TrySelectFirst(ParseJson("[1]"), out _));
	}

	[TestMethod]
	public void EvaluatesAbsoluteQueriesInNestedFiltersFromTheRoot()
		=> Assert.AreEqual("""[{"k":"b"}]""", Select("""{"key":"b","items":[{"k":"a"},{"k":"b"}]}""", "$.items[?@.k == $.key]"));

	[TestMethod]
	public void EvaluatesFiltersOnObjectMembers()
		=> Assert.AreSequenceEqual(new[] { "$['b']", "$['c']" }, Paths("""{"a":1,"b":2,"c":3}""", "$[?@ >= 2]"));
}
