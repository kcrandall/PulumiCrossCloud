using System.Collections.Immutable;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;

namespace PulumiCrossCloud.Bird.Provider;

/// <summary>
/// Builds a <see cref="DiffResponse"/> by comparing old and new inputs property by property.
/// Optional inputs that are omitted from the new inputs are left unmanaged rather than reset.
/// </summary>
internal static class InputDiff
{
    public static DiffResponse Compute(
        DiffRequest request,
        IReadOnlyList<string> properties,
        IReadOnlySet<string> replaceProperties,
        IReadOnlySet<string> requiredProperties
    )
    {
        var olds = request.OldInputs.IsEmpty ? request.OldOutputs : request.OldInputs;
        var ignored = request.IgnoreChanges.IsDefault
            ? ImmutableHashSet<string>.Empty
            : request.IgnoreChanges.ToImmutableHashSet();

        List<string> diffs = [];
        List<string> replaces = [];
        Dictionary<string, PropertyDiff> detailed = [];
        foreach (var property in properties)
        {
            if (ignored.Contains(property))
                continue;

            var newValue = PropertyValues.Get(request.NewInputs, property);
            if (newValue.IsNull && !requiredProperties.Contains(property))
                continue;

            var oldValue = PropertyValues.Get(olds, property);
            if (PropertyValues.SameValue(oldValue, newValue))
                continue;

            var replace = replaceProperties.Contains(property);
            diffs.Add(property);
            if (replace)
                replaces.Add(property);
            detailed[property] = new PropertyDiff
            {
                Kind = (oldValue.IsNull, replace) switch
                {
                    (true, true) => PropertyDiffKind.AddReplace,
                    (true, false) => PropertyDiffKind.Add,
                    (false, true) => PropertyDiffKind.UpdateReplace,
                    (false, false) => PropertyDiffKind.Update,
                },
                InputDiff = true,
            };
        }

        return new DiffResponse
        {
            Changes = diffs.Count > 0,
            Diffs = diffs,
            Replaces = replaces,
            Stables = [],
            DetailedDiff = detailed,
            DeleteBeforeReplace = false,
        };
    }

    /// <summary>
    /// True when the property was supplied in <paramref name="news"/> and differs from <paramref name="olds"/>.
    /// </summary>
    public static bool Changed(
        ImmutableDictionary<string, PropertyValue> olds,
        ImmutableDictionary<string, PropertyValue> news,
        string property
    )
    {
        var newValue = PropertyValues.Get(news, property);
        return !newValue.IsNull
            && !PropertyValues.SameValue(PropertyValues.Get(olds, property), newValue);
    }
}
