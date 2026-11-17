using BenchmarkDotNet.Attributes;

namespace PeterJuhasz.Text.Json.Path.Benchmarks;

/// <summary>
/// Measures parsing queries into expression trees, from a single name up to filters with functions and nested queries.
/// </summary>
[MemoryDiagnoser]
public class ParseBenchmarks
{
	[Params(
		"$.store.book[0].title",
		"$..book[-1:]['author', 'title']",
		"$.store.book[?@.price < 10 && @.category == 'fiction'].title",
		"$..book[?match(@.isbn, '0-[0-9]{3}-.*') || count(@.tags[?@ == 't1']) > 0]",
		"$[?@.a[?@.b[?@.c == $.d]]]")]
	public string Query { get; set; } = null!;

	[Benchmark]
	public JsonPathQuery Parse() => JsonPathQuery.Parse(Query);
}
