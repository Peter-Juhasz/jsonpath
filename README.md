# JSONPath

A [JSONPath (RFC 9535)](https://www.rfc-editor.org/info/rfc9535/) implementation for .NET over the `System.Text.Json` primitives `JsonDocument` and `JsonElement`.
Queries are parsed into an immutable expression tree, which is then evaluated on documents without copying them into another model.

It passes the whole [JSONPath Compliance Test Suite](https://github.com/jsonpath-standard/jsonpath-compliance-test-suite).

## Querying

Query a document or an element, with the query as a string:

```cs
using var document = JsonDocument.Parse(json);

IReadOnlyList<JsonElement> authors = document.SelectElements("$.store.book[*].author");
IReadOnlyList<JsonElement> cheap = document.SelectElements("$..book[?@.price < 10]");

if (document.TrySelectFirst("$..book[?@.isbn]", out var book))
{
	// ...
}
```

Or parse the query once, and evaluate it many times; queries are immutable and thread-safe:

```cs
var query = JsonPathQuery.Parse("$..book[?match(@.category, 'fic.*')].title");

var titles = query.SelectElements(document.RootElement);
```

`TryParse` returns false instead of throwing for invalid queries, with a message that tells why and where:

```cs
if (!JsonPathQuery.TryParse(text, out var query, out var error))
{
	// "'$[?@.* == 1]' is not a valid JSONPath query: only singular queries can be compared at position 3."
}
```

## Locations

Select the nodes with their locations, as [normalized paths](https://www.rfc-editor.org/rfc/rfc9535.html#name-normalized-paths):

```cs
foreach (var node in document.SelectNodes("$..author"))
{
	Console.WriteLine($"{node.Location}: {node.Value}"); // $['store']['book'][0]['author']: Nigel Rees
}
```

A `NormalizedPath` can also be converted to a JSON Pointer (RFC 6901) with `ToJsonPointer()`, or back to a query with `ToQuery()`.
Selecting only the values with `SelectElements` is faster, as it does not build the paths.

## Expression tree

Parsed queries are trees of records, which can be inspected, compared, and written back as text:

```cs
var query = JsonPathQuery.Parse("$.store['book'][?@.price<10]");

// JsonPathQuery
//   ChildSegment: NameSelector("store")
//   ChildSegment: NameSelector("book")
//   ChildSegment: FilterSelector(ComparisonExpression(QueryExpression(@.price), LessThan, LiteralExpression(10)))

Console.WriteLine(query); // $.store.book[?@.price < 10]
```

Trees can be built in code too; their constructors check that they are well-typed, like the parser does:

```cs
var price = new QueryExpression(JsonPathQuery.Current.Child("price"));
var query = JsonPathQuery.Root
	.Descendant(new NameSelector("book"))
	.Child(new FilterSelector(
		new ComparisonExpression(price, ComparisonOperator.LessThan, LiteralExpression.Number(10))
		& !new QueryExpression(JsonPathQuery.Current.Child("isbn"))));

// $..book[?@.price < 10 && !@.isbn]
```

## Supported syntax

Everything RFC 9535 defines:

| Syntax | Description |
| --- | --- |
| `$` | the root node |
| `.name`, `['name']` | a member of an object |
| `[0]`, `[-1]` | an element of an array, counted from the end if negative |
| `[1:5:2]`, `[::-1]` | a slice of an array |
| `.*`, `[*]` | all members or elements |
| `..name`, `..[...]`, `..*` | the same, at any depth |
| `['a', 0, 1:3]` | several selectors at once |
| `[?<expression>]` | the members or elements for which the expression is true |
| `@` | the current node in a filter |
| `==`, `!=`, `<`, `<=`, `>`, `>=` | comparisons |
| `&&`, `\|\|`, `!`, `( )` | logical operators |
| `length()`, `count()`, `value()`, `match()`, `search()` | functions |

Comparisons follow the RFC: numbers are compared by value, so `1 == 1.0`, beyond the precision of doubles where they can be; strings by their Unicode scalar values;
arrays and objects by their contents; and a query that selects nothing is only equal to another one that selects nothing.

`match()` and `search()` take [I-Regexp (RFC 9485)](https://www.rfc-editor.org/info/rfc9485/) patterns, which are translated to .NET regular expressions
and run with `RegexOptions.NonBacktracking`, so matching takes linear time whatever the pattern. Patterns that are not I-Regexp, like `\d`, match nothing.
Characters outside the Basic Multilingual Plane are matched as single characters, but Unicode categories like `\p{L}` only know the characters inside it.

## Performance

Evaluation walks the `JsonElement`s depth first, passing each selected node on to the next segment right away, so it does not build lists for the segments in between,
and it stops as soon as it has what it needs, like for `TrySelectFirst` and for existence tests in filters. Names are looked up as UTF-8, strings are compared
without decoding them where they have no escapes, and literal patterns are compiled once per query.

Run the benchmarks, including a comparison with JsonPath.Net and Newtonsoft.Json:

```
dotnet run -c Release --project benchmarks/PeterJuhasz.Text.Json.Path.Benchmarks -- --filter '*'
```
