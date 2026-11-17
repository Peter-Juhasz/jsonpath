using BenchmarkDotNet.Attributes;
using System.Text.Json;

namespace PeterJuhasz.Text.Json.Path.Benchmarks;

/// <summary>
/// Measures evaluating parsed queries on the <see cref="SampleDocument"/>: selecting the values only, which is the fastest,
/// selecting the nodes with their locations, which allocates a path for each, and stopping at the first node.
/// </summary>
[MemoryDiagnoser]
public class SelectBenchmarks
{
	private JsonDocument _document = null!;

	private JsonPathQuery _query = null!;

	[Params(
		"$.store.book[500].title",
		"$.store.book[*].author",
		"$..author",
		"$.store.book[?@.price < 10].title",
		"$.store.book[?match(@.category, 'fic.*')].title",
		"$.store.book[?@.reviews[?@.stars == 5]].title",
		"$.store.book[::-2].title",
		"$..*")]
	public string Query { get; set; } = null!;

	[GlobalSetup]
	public void Setup()
	{
		_document = JsonDocument.Parse(SampleDocument.Json);
		_query = JsonPathQuery.Parse(Query);
	}

	[GlobalCleanup]
	public void Cleanup() => _document.Dispose();

	[Benchmark(Baseline = true)]
	public int SelectElements() => _query.SelectElements(_document.RootElement).Count;

	[Benchmark]
	public int SelectNodes() => _query.SelectNodes(_document.RootElement).Count;

	[Benchmark]
	public bool TrySelectFirst() => _query.TrySelectFirst(_document.RootElement, out _);
}
