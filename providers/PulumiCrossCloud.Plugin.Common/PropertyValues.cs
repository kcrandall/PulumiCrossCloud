using System.Collections.Immutable;
using Pulumi.Experimental;

namespace PulumiCrossCloud.Plugin.Common;

/// <summary>
/// Helpers for reading and writing Pulumi property maps.
/// </summary>
public static class PropertyValues
{
    public static readonly ImmutableDictionary<string, PropertyValue> Empty = ImmutableDictionary<
        string,
        PropertyValue
    >.Empty;

    public static PropertyValue Get(ImmutableDictionary<string, PropertyValue>? map, string key) =>
        map is not null && map.TryGetValue(key, out var value) ? value : PropertyValue.Null;

    public static bool IsComputed(ImmutableDictionary<string, PropertyValue>? map, string key) =>
        Get(map, key).IsComputed;

    public static string? GetString(ImmutableDictionary<string, PropertyValue>? map, string key) =>
        Get(map, key).TryGetString(out var value) ? value : null;

    public static bool? GetBool(ImmutableDictionary<string, PropertyValue>? map, string key) =>
        Get(map, key).TryGetBool(out var value) ? value : null;

    public static double? GetNumber(ImmutableDictionary<string, PropertyValue>? map, string key) =>
        Get(map, key).TryGetNumber(out var value) ? value : null;

    public static PropertyValue Number(double? value) =>
        value is null ? PropertyValue.Null : new PropertyValue(value.Value);

    public static PropertyValue String(string? value) =>
        value is null ? PropertyValue.Null : new PropertyValue(value);

    public static PropertyValue Bool(bool? value) =>
        value is null ? PropertyValue.Null : new PropertyValue(value.Value);

    /// <summary>
    /// Compares two input values semantically, ignoring secret and dependency metadata.
    /// An unknown (computed) value is always treated as a change.
    /// </summary>
    public static bool SameValue(PropertyValue left, PropertyValue right)
    {
        if (left.IsComputed || right.IsComputed)
            return false;
        if (left.IsNull || right.IsNull)
            return left.IsNull && right.IsNull;
        if (left.TryGetString(out var leftString) && right.TryGetString(out var rightString))
            return leftString == rightString;
        if (left.TryGetBool(out var leftBool) && right.TryGetBool(out var rightBool))
            return leftBool == rightBool;
        if (left.TryGetNumber(out var leftNumber) && right.TryGetNumber(out var rightNumber))
            return leftNumber.Equals(rightNumber);
        if (left.TryGetArray(out var leftArray) && right.TryGetArray(out var rightArray))
            return leftArray.Length == rightArray.Length
                && leftArray.Zip(rightArray).All(pair => SameValue(pair.First, pair.Second));
        if (left.TryGetMap(out var leftMap) && right.TryGetMap(out var rightMap))
            return leftMap.Count == rightMap.Count
                && leftMap.All(pair =>
                    rightMap.TryGetValue(pair.Key, out var other) && SameValue(pair.Value, other)
                );

        return false;
    }
}

public static class PropertyMapExtensions
{
    public static ImmutableDictionary<string, PropertyValue> SetItems(
        this ImmutableDictionary<string, PropertyValue> map,
        IEnumerable<string> keys,
        PropertyValue value
    ) => map.SetItems(keys.Select(key => KeyValuePair.Create(key, value)));
}
