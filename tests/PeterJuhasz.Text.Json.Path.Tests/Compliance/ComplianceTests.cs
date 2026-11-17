namespace PeterJuhasz.Text.Json.Path.Tests.Compliance;

/// <summary>
/// Runs the JSONPath Compliance Test Suite (https://github.com/jsonpath-standard/jsonpath-compliance-test-suite), which covers RFC 9535 test by test.
/// Each test either has an invalid selector, or a document with the expected nodes; where the order of members is not defined, any of several results may be expected.
/// </summary>
[TestClass]
public sealed class ComplianceTests
{
	private static readonly JsonElement[] Tests = LoadTests();

	private static JsonElement[] LoadTests()
	{
		var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Compliance", "cts.json");
		using var document = JsonDocument.Parse(File.ReadAllBytes(path));
		return [.. document.RootElement.GetProperty("tests").EnumerateArray().Select(test => test.Clone())];
	}

	public static IEnumerable<TestDataRow<int>> AllTests => Tests.Select((test, index) => new TestDataRow<int>(index) { DisplayName = test.GetProperty("name").GetString() });

	public static IEnumerable<TestDataRow<int>> ValidTests => AllTests.Where(row => !IsInvalid(Tests[row.Value]));

	private static bool IsInvalid(JsonElement test) => test.TryGetProperty("invalid_selector", out var invalid) && invalid.GetBoolean();

	[TestMethod]
	[DynamicData(nameof(AllTests))]
	public void Passes(int index)
	{
		var test = Tests[index];
		var selector = test.GetProperty("selector").GetString()!;
		if (IsInvalid(test))
		{
			TestHelpers.AssertInvalid(selector);
			return;
		}

		var document = test.GetProperty("document");
		var query = JsonPathQuery.Parse(selector);
		var nodes = query.SelectNodes(document);
		var values = nodes.Select(node => node.Value).ToArray();
		var paths = nodes.Select(node => node.Location.ToString()).ToArray();

		if (test.TryGetProperty("result", out var result))
		{
			AssertValues(result, values);
			AssertPaths(test.GetProperty("result_paths"), paths);
		}
		else
		{
			var alternatives = test.GetProperty("results").EnumerateArray().ToArray();
			var alternativePaths = test.GetProperty("results_paths").EnumerateArray().ToArray();
			var matching = Enumerable.Range(0, alternatives.Length).Where(i => ValuesEqual(alternatives[i], values) && PathsEqual(alternativePaths[i], paths));
			Assert.IsNotEmpty(matching, $"'{selector}' selected {TestHelpers.ToJson(values)} at {string.Join(", ", paths)}, which is none of the expected results.");
		}

		// selecting only the values, or only the first one, must agree with selecting the nodes
		Assert.AreEqual(TestHelpers.ToJson(values), TestHelpers.ToJson(query.SelectElements(document)));
		Assert.AreEqual(values.Length > 0, query.TrySelectFirst(document, out var first));
		if (values.Length > 0)
		{
			Assert.IsTrue(JsonElement.DeepEquals(values[0], first));
		}
	}

	[TestMethod]
	[DynamicData(nameof(ValidTests))]
	public void RoundTrips(int index)
	{
		var test = Tests[index];
		var query = JsonPathQuery.Parse(test.GetProperty("selector").GetString()!);
		var written = query.ToString();
		var reparsed = JsonPathQuery.Parse(written);

		Assert.AreEqual(query, reparsed, $"'{written}' was parsed differently.");
		Assert.AreEqual(query.GetHashCode(), reparsed.GetHashCode());
		Assert.AreEqual(written, reparsed.ToString());

		var document = test.GetProperty("document");
		Assert.AreEqual(TestHelpers.ToJson(query.SelectElements(document)), TestHelpers.ToJson(reparsed.SelectElements(document)));
	}

	[TestMethod]
	[DynamicData(nameof(ValidTests))]
	public void NormalizedPathsSelectTheirNodes(int index)
	{
		var test = Tests[index];
		var document = test.GetProperty("document");
		foreach (var node in JsonPathQuery.Parse(test.GetProperty("selector").GetString()!).SelectNodes(document))
		{
			var query = JsonPathQuery.Parse(node.Location.ToString());
			Assert.IsTrue(query.IsSingular);
			Assert.AreEqual(node.Location.ToQuery(), query);
			Assert.IsTrue(query.TrySelectFirst(document, out var value));
			Assert.IsTrue(JsonElement.DeepEquals(node.Value, value));
		}
	}

	private static void AssertValues(JsonElement expected, JsonElement[] actual)
		=> Assert.IsTrue(ValuesEqual(expected, actual), $"Expected {expected.GetRawText()} but selected {TestHelpers.ToJson(actual)}.");

	private static void AssertPaths(JsonElement expected, string[] actual)
		=> Assert.IsTrue(PathsEqual(expected, actual), $"Expected {expected.GetRawText()} but selected at {string.Join(", ", actual)}.");

	private static bool ValuesEqual(JsonElement expected, JsonElement[] actual)
		=> expected.GetArrayLength() == actual.Length && expected.EnumerateArray().Zip(actual).All(pair => JsonElement.DeepEquals(pair.First, pair.Second));

	private static bool PathsEqual(JsonElement expected, string[] actual)
		=> expected.EnumerateArray().Select(path => path.GetString()).SequenceEqual(actual);
}
