using System.Collections.Immutable;

namespace PeterJuhasz.Text.Json.Path;

/// <summary>
/// Argument checks and item-by-item comparison of the arrays that the records of the expression tree hold, as records compare <see cref="ImmutableArray{T}"/> by reference.
/// </summary>
internal static class Guard
{
	public static ImmutableArray<T> NotDefault<T>(ImmutableArray<T> array) => array.IsDefault ? throw new ArgumentException("The array must be initialized.", nameof(array)) : array;

	public static ImmutableArray<T> NotEmpty<T>(ImmutableArray<T> array) => NotDefault(array).IsEmpty ? throw new ArgumentException("The array must not be empty.", nameof(array)) : array;

	public static bool ItemsEqual<T>(ImmutableArray<T> left, ImmutableArray<T> right) where T : IEquatable<T>
		=> left.AsSpan().SequenceEqual(right.AsSpan());

	public static int GetItemsHashCode<T>(ImmutableArray<T> items)
	{
		var hash = new HashCode();
		foreach (var item in items)
		{
			hash.Add(item);
		}

		return hash.ToHashCode();
	}
}
