using PeterJuhasz.Text.Json.Path.Expressions;
using System.Globalization;
using System.Text;

namespace PeterJuhasz.Text.Json.Path;

/// <summary>
/// The location of a node in a JSON document, as a normalized path (RFC 9535, section 2.7): the member names and array indices that lead to it from the root,
/// like <c>$['store']['book'][0]</c>. Paths share their parents, so the locations of the nodes selected by a query take little memory.
/// </summary>
public sealed class NormalizedPath : IEquatable<NormalizedPath>
{
	private NormalizedPath(NormalizedPath? parent, string? memberName, int arrayIndex)
	{
		Parent = parent;
		MemberName = memberName;
		_arrayIndex = arrayIndex;
		Depth = parent is null ? 0 : parent.Depth + 1;
	}

	private readonly int _arrayIndex;

	/// <summary>
	/// The path of the root node, <c>$</c>.
	/// </summary>
	public static NormalizedPath Root { get; } = new(parent: null, memberName: null, arrayIndex: -1);

	/// <summary>
	/// The path of the object or array that contains the node; null for the root.
	/// </summary>
	public NormalizedPath? Parent { get; }

	/// <summary>
	/// The name of the member that holds the node, if its parent is an object.
	/// </summary>
	public string? MemberName { get; }

	/// <summary>
	/// The index of the node in its parent, if its parent is an array.
	/// </summary>
	public int? ArrayIndex => _arrayIndex >= 0 ? _arrayIndex : null;

	/// <summary>
	/// The number of member names and array indices in the path; zero for the root.
	/// </summary>
	public int Depth { get; }

	/// <summary>
	/// Whether this is the path of the root node.
	/// </summary>
	public bool IsRoot => Parent is null;

	/// <summary>
	/// Returns the path of a member of the object at this path.
	/// </summary>
	public NormalizedPath Append(string memberName)
	{
		ArgumentNullException.ThrowIfNull(memberName);
		return new(this, memberName, arrayIndex: -1);
	}

	/// <summary>
	/// Returns the path of an element of the array at this path.
	/// </summary>
	public NormalizedPath Append(int arrayIndex)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
		return new(this, memberName: null, arrayIndex);
	}

	/// <summary>
	/// Returns the singular query that selects the node at this path, like <c>$.store.book[0]</c>.
	/// </summary>
	public JsonPathQuery ToQuery()
	{
		var segments = new JsonPathSegment[Depth];
		for (var path = this; path.Parent is not null; path = path.Parent)
		{
			JsonPathSelector selector = path.MemberName is { } name ? new NameSelector(name) : new IndexSelector(path._arrayIndex);
			segments[path.Depth - 1] = new ChildSegment([selector]);
		}

		return new JsonPathQuery([.. segments]);
	}

	/// <summary>
	/// Returns the path as a JSON Pointer (RFC 6901), like <c>/store/book/0</c>; the root is the empty string.
	/// </summary>
	public string ToJsonPointer()
	{
		using var pooled = StringBuilderPool.GetPooledObject(out var builder);
		foreach (var path in GetPaths())
		{
			builder.Append('/');
			if (path.MemberName is { } name)
			{
				foreach (var c in name)
				{
					switch (c)
					{
						case '~':
							builder.Append("~0");
							break;

						case '/':
							builder.Append("~1");
							break;

						default:
							builder.Append(c);
							break;
					}
				}
			}
			else
			{
				builder.Append(path._arrayIndex);
			}
		}

		return builder.ToString();
	}

	/// <summary>
	/// Returns the normalized path, like <c>$['store']['book'][0]</c>: names are always written in brackets and single quotes,
	/// with apostrophes, backslashes and control characters escaped, so equal locations are always written the same way.
	/// </summary>
	public override string ToString()
	{
		using var pooled = StringBuilderPool.GetPooledObject(out var builder);
		builder.Append('$');
		foreach (var path in GetPaths())
		{
			builder.Append('[');
			if (path.MemberName is { } name)
			{
				SyntaxFacts.AppendStringLiteral(builder, name);
			}
			else
			{
				builder.Append(path._arrayIndex.ToString(CultureInfo.InvariantCulture));
			}

			builder.Append(']');
		}

		return builder.ToString();
	}

	/// <summary>
	/// Returns the paths from the first member or element up to this one, excluding the root.
	/// </summary>
	private NormalizedPath[] GetPaths()
	{
		var paths = new NormalizedPath[Depth];
		for (var path = this; path.Parent is not null; path = path.Parent)
		{
			paths[path.Depth - 1] = path;
		}

		return paths;
	}

	/// <summary>
	/// Compares the member names and indices along the paths.
	/// </summary>
	public bool Equals(NormalizedPath? other)
	{
		if (other is null || Depth != other.Depth)
		{
			return false;
		}

		for (NormalizedPath? left = this, right = other; left is not null && right is not null; left = left.Parent, right = right.Parent)
		{
			if (ReferenceEquals(left, right))
			{
				return true;
			}

			if (left._arrayIndex != right._arrayIndex || !string.Equals(left.MemberName, right.MemberName, StringComparison.Ordinal))
			{
				return false;
			}
		}

		return true;
	}

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is NormalizedPath other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();
		for (var path = this; path.Parent is not null; path = path.Parent)
		{
			hash.Add(path._arrayIndex);
			hash.Add(path.MemberName, StringComparer.Ordinal);
		}

		return hash.ToHashCode();
	}

	/// <summary>
	/// Compares two paths with <see cref="Equals(NormalizedPath?)"/>.
	/// </summary>
	public static bool operator ==(NormalizedPath? left, NormalizedPath? right) => left is null ? right is null : left.Equals(right);

	/// <summary>
	/// Compares two paths with <see cref="Equals(NormalizedPath?)"/>.
	/// </summary>
	public static bool operator !=(NormalizedPath? left, NormalizedPath? right) => !(left == right);
}
