using static PeterJuhasz.Text.Json.Path.Tests.TestHelpers;

namespace PeterJuhasz.Text.Json.Path.Tests;

[TestClass]
public sealed class NormalizedPathTests
{
	[TestMethod]
	[DataRow("a'b", "$['a\\'b']")]
	[DataRow("a\\b", "$['a\\\\b']")]
	[DataRow("a\"b", "$['a\"b']")]
	[DataRow("\b\f\n\r\t", "$['\\b\\f\\n\\r\\t']")]
	[DataRow("\u0001\u001f", "$['\\u0001\\u001f']")]
	[DataRow("\u007f😀", "$['\u007f😀']")]
	[DataRow("", "$['']")]
	public void EscapesMemberNames(string name, string expected)
	{
		var path = NormalizedPath.Root.Append(name);

		Assert.AreEqual(expected, path.ToString());
		Assert.AreEqual(path, JsonPathQuery.Parse(expected).SelectNodes(ParseJson($$"""{{{JsonSerializer.Serialize(name)}}:1}""")).Single().Location);
	}

	[TestMethod]
	public void WritesJsonPointers()
	{
		Assert.AreEqual("", NormalizedPath.Root.ToJsonPointer());
		Assert.AreEqual("/a~1b/m~0n/0", NormalizedPath.Root.Append("a/b").Append("m~n").Append(0).ToJsonPointer());
	}

	[TestMethod]
	public void DescribesEachStep()
	{
		var path = NormalizedPath.Root.Append("a").Append(2);

		Assert.AreEqual(2, path.Depth);
		Assert.AreEqual(2, path.ArrayIndex);
		Assert.IsNull(path.MemberName);
		Assert.AreEqual("a", path.Parent!.MemberName);
		Assert.IsNull(path.Parent.ArrayIndex);
		Assert.IsTrue(path.Parent.Parent!.IsRoot);
		Assert.AreSame(NormalizedPath.Root, path.Parent.Parent);
	}

	[TestMethod]
	public void ComparesPathsStepByStep()
	{
		var path = NormalizedPath.Root.Append("a").Append(0);

		Assert.AreEqual(NormalizedPath.Root.Append("a").Append(0), path);
		Assert.AreEqual(NormalizedPath.Root.Append("a").Append(0).GetHashCode(), path.GetHashCode());
		Assert.AreNotEqual(NormalizedPath.Root.Append("a").Append(1), path);
		Assert.AreNotEqual(NormalizedPath.Root.Append("0").Append(0), path);
		Assert.AreNotEqual(NormalizedPath.Root.Append("a"), path);
		Assert.IsTrue(path == NormalizedPath.Root.Append("a").Append(0));
		Assert.IsTrue(path != NormalizedPath.Root);
	}

	[TestMethod]
	public void ConvertsToASingularQuery()
	{
		var query = NormalizedPath.Root.Append("a b").Append(0).ToQuery();

		Assert.IsTrue(query.IsSingular);
		Assert.AreEqual("$['a b'][0]", query.ToString());
		Assert.AreEqual(JsonPathQuery.Root, NormalizedPath.Root.ToQuery());
	}

	[TestMethod]
	public void SharesTheParentsOfSiblings()
	{
		var nodes = JsonPathQuery.Parse("$.a.*").SelectNodes(ParseJson("""{"a":[1,2]}"""));

		Assert.AreSame(nodes[0].Location.Parent, nodes[1].Location.Parent);
	}
}
