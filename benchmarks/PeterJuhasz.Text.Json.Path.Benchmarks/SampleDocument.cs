using System.Globalization;
using System.Text;

namespace PeterJuhasz.Text.Json.Path.Benchmarks;

/// <summary>
/// The document the benchmarks query: the bookstore of RFC 9535, with many books, so that queries have something to walk.
/// It is generated rather than read from a file, the same on every run.
/// </summary>
internal static class SampleDocument
{
	public const int BookCount = 1_000;

	public static string Json { get; } = Create();

	private static string Create()
	{
		string[] categories = ["reference", "fiction", "poetry", "science"];
		var random = new Random(9535);
		var builder = new StringBuilder();
		builder.Append("""{"store":{"book":[""");
		for (var i = 0; i < BookCount; i++)
		{
			if (i > 0)
			{
				builder.Append(',');
			}

			builder.Append(CultureInfo.InvariantCulture, $$"""{"category":"{{categories[i % categories.Length]}}","author":"Author {{i}}","title":"Title {{i}}","price":{{random.Next(100, 5000) / 100.0}}""");
			if (i % 3 == 0)
			{
				builder.Append(CultureInfo.InvariantCulture, $$""","isbn":"0-{{random.Next(100, 999)}}-{{random.Next(10000, 99999)}}-{{i % 10}}" """);
			}

			builder.Append(CultureInfo.InvariantCulture, $$""","tags":["t{{i % 7}}","t{{i % 11}}"],"reviews":[{"stars":{{(i % 5) + 1}},"text":"Review of book {{i}}"}]}""");
		}

		builder.Append("""],"bicycle":{"color":"red","price":399}}}""");
		return builder.ToString();
	}
}
