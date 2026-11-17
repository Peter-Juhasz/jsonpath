using BenchmarkDotNet.Attributes;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PeterJuhasz.Text.Json.Path.Benchmarks;

/// <summary>
/// Compares evaluating parsed queries on the <see cref="SampleDocument"/> with other JSONPath libraries for .NET, each on its own document model:
/// JsonPath.Net on <see cref="JsonNode"/>, which implements RFC 9535 too, and Newtonsoft.Json on <see cref="JToken"/>, which predates it and needs filters in parentheses.
/// The documents and queries are parsed once in the setup, so only the evaluation is measured.
/// </summary>
[MemoryDiagnoser]
public class ComparisonBenchmarks
{
	/// <summary>
	/// The queries of each scenario: in RFC 9535 syntax, and in Newtonsoft.Json syntax.
	/// </summary>
	private static readonly Dictionary<string, (string Rfc, string Newtonsoft)> Scenarios = new()
	{
		["Path"] = ("$.store.book[500].title", "$.store.book[500].title"),
		["Wildcard"] = ("$.store.book[*].author", "$.store.book[*].author"),
		["Descendants"] = ("$..author", "$..author"),
		["Filter"] = ("$.store.book[?@.price < 10].title", "$.store.book[?(@.price < 10)].title"),
	};

	private JsonDocument _document = null!;

	private JsonPathQuery _query = null!;

	private JsonNode _node = null!;

	private global::Json.Path.JsonPath _jsonPathNet = null!;

	private JToken _token = null!;

	private string _newtonsoftQuery = null!;

	[Params("Path", "Wildcard", "Descendants", "Filter")]
	public string Scenario { get; set; } = null!;

	[GlobalSetup]
	public void Setup()
	{
		var (rfc, newtonsoft) = Scenarios[Scenario];
		_document = JsonDocument.Parse(SampleDocument.Json);
		_query = JsonPathQuery.Parse(rfc);
		_node = JsonNode.Parse(SampleDocument.Json)!;
		_jsonPathNet = global::Json.Path.JsonPath.Parse(rfc);
		_token = JToken.Parse(SampleDocument.Json);
		_newtonsoftQuery = newtonsoft;

		var expected = SelectElements();
		if (JsonPathNet() != expected || Newtonsoft() != expected)
		{
			throw new InvalidOperationException($"The libraries select different numbers of nodes for '{Scenario}'.");
		}
	}

	[GlobalCleanup]
	public void Cleanup() => _document.Dispose();

	[Benchmark(Baseline = true)]
	public int SelectElements() => _query.SelectElements(_document.RootElement).Count;

	[Benchmark]
	public int SelectNodes() => _query.SelectNodes(_document.RootElement).Count;

	[Benchmark]
	public int JsonPathNet() => _jsonPathNet.Evaluate(_node).Matches.Count;

	[Benchmark]
	public int Newtonsoft() => _token.SelectTokens(_newtonsoftQuery).Count();
}
