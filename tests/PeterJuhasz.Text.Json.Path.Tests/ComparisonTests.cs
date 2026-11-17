using static PeterJuhasz.Text.Json.Path.Tests.TestHelpers;

namespace PeterJuhasz.Text.Json.Path.Tests;

[TestClass]
public sealed class ComparisonTests
{
	[TestMethod]
	public void ComparesNumbersByTheirValue()
		=> Assert.AreEqual("""[1,1.0,1e0,10e-1,100E-2]""", Select("""[1,1.0,1e0,10e-1,100E-2,"1",true,[1]]""", "$[?@ == 1]"));

	[TestMethod]
	[DataRow("$[?@ > 1e300]", "[1e400]")]
	[DataRow("$[?@ == 1e400]", "[1e400]")]
	[DataRow("$[?@ < 2e-50]", "[1e-50]")]
	[DataRow("$[?@ < 0.3]", "[1e-50,3e-50,0.1]")]
	[DataRow("$[?@ > 9007199254740993]", "[1e400,9007199254740994]")]
	public void ComparesNumbersBeyondThePrecisionOfDoubles(string query, string expected)
		=> Assert.AreEqual(expected, Select("[1e400,1e-50,3e-50,0.1,0.30000000000000001,9007199254740993,9007199254740994]", query));

	[TestMethod]
	public void ComparesStringsAfterUnescaping()
		=> Assert.AreEqual("""["a","a"]""", Select("""["a","\u0061","b"]""", "$[?@ == '\\u0061']"));

	[TestMethod]
	[DataRow("""["😀","\uffff","a"]""")]
	[DataRow("""["\ud83d\ude00","\uffff","a"]""")]
	public void OrdersStringsByUnicodeScalarValuesRatherThanUtf16CodeUnits(string json)
	{
		Assert.AreEqual("""["😀"]""", Select(json, "$[?@ > '\\uffff']"));
		Assert.AreEqual("[\"\uffff\",\"a\"]", Select(json, "$[?@ < '😀']"));
	}

	[TestMethod]
	public void ComparesArraysAndObjectsByTheirContents()
	{
		const string json = """{"x":{"a":[1,2],"b":null},"items":[{"b":null,"a":[1,2.0]},{"a":[2,1],"b":null},{"a":[1,2]}]}""";

		Assert.AreSequenceEqual(new[] { "$['items'][0]" }, Paths(json, "$.items[?@ == $.x]"));
		Assert.AreSequenceEqual(new[] { "$['items'][0]" }, Paths(json, "$.items[?@ <= $.x]"));
		Assert.AreEqual("[]", Select(json, "$.items[?@ < $.x]"));
	}

	[TestMethod]
	[DataRow("$[?@.missing == $.alsoMissing]", "[1,2]")]
	[DataRow("$[?@.missing != 1]", "[1,2]")]
	[DataRow("$[?@.missing == null]", "[]")]
	[DataRow("$[?@.missing < 1]", "[]")]
	[DataRow("$[?@.missing <= $.alsoMissing]", "[1,2]")]
	public void ComparesNothingOnlyEqualToNothing(string query, string expected) => Assert.AreEqual(expected, Select("[1,2]", query));

	[TestMethod]
	[DataRow("$[?@ == true]", "[true]")]
	[DataRow("$[?@ == null]", "[null]")]
	[DataRow("$[?@ <= true]", "[true]")]
	[DataRow("$[?@ < true]", "[]")]
	[DataRow("$[?@ > false]", "[]")]
	public void DoesNotOrderBooleansAndNull(string query, string expected) => Assert.AreEqual(expected, Select("[true,false,null,0,\"\"]", query));

	[TestMethod]
	public void ComparesTheResultsOfFunctionsWithNumbers()
		=> Assert.AreEqual("""["ab"]""", Select("""["a","ab","abc"]""", "$[?length(@) == 2.0 && length(@) > 1.5 && 2e0 >= length(@)]"));
}
