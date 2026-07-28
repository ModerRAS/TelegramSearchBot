namespace Moder.Update.Models;

/// <summary>
/// Utility for version range checks and update path computation.
/// </summary>
public static class VersionRange
{
    /// <summary>
    /// Checks whether <paramref name="version"/> is within [<paramref name="min"/>, <paramref name="max"/>].
    /// If <paramref name="max"/> is null, only the lower bound is checked.
    /// </summary>
    public static bool Contains(Version version, Version min, Version? max)
    {
        if (version < min)
            return false;
        if (max is not null && version > max)
            return false;
        return true;
    }

    /// <summary>
    /// Determines whether there is a valid update path from <paramref name="current"/> to <paramref name="target"/>
    /// through the provided manifests.
    /// </summary>
    public static bool CanReach(Version current, Version target, IEnumerable<UpdateManifest> chain)
    {
        return GetUpdatePath(current, target, chain).Any();
    }

    /// <summary>
    /// Computes the ordered sequence of manifests to apply to go from <paramref name="from"/> to <paramref name="to"/>.
    /// Prefers cumulative packages when available.
    /// </summary>
    public static IReadOnlyList<UpdateManifest> GetUpdatePath(
        Version from, Version to, IEnumerable<UpdateManifest> chain)
    {
        var manifests = chain.ToList();
        var path = new List<UpdateManifest>();
        var current = from;

        while (current < to)
        {
            UpdateManifest? cumulative = null;
            foreach (var m in manifests.Where(m => m.IsCumulative))
            {
                if (!Version.TryParse(m.TargetVersion, out var tv) || tv > to)
                    continue;
                if (!Version.TryParse(m.MinSourceVersion, out var minV))
                    continue;
                Version? maxV = null;
                if (m.MaxSourceVersion is not null && Version.TryParse(m.MaxSourceVersion, out var mv))
                    maxV = mv;
                if (!Contains(current, minV, maxV))
                    continue;
                if (cumulative is null || Version.Parse(m.TargetVersion) > Version.Parse(cumulative.TargetVersion))
                    cumulative = m;
            }

            if (cumulative is not null)
            {
                path.Add(cumulative);
                current = Version.Parse(cumulative.TargetVersion);
                continue;
            }

            UpdateManifest? next = null;
            foreach (var m in manifests)
            {
                if (!Version.TryParse(m.TargetVersion, out var tv) || tv > to || tv <= current)
                    continue;
                if (!Version.TryParse(m.MinSourceVersion, out var minV))
                    continue;
                Version? maxV = null;
                if (m.MaxSourceVersion is not null && Version.TryParse(m.MaxSourceVersion, out var mv))
                    maxV = mv;
                if (!Contains(current, minV, maxV))
                    continue;
                if (next is null || tv < Version.Parse(next.TargetVersion))
                    next = m;
            }

            if (next is null)
                break;

            path.Add(next);
            current = Version.Parse(next.TargetVersion);
        }

        if (current < to)
            return [];

        return path;
    }

    /// <summary>
    /// Computes the update path using catalog entries instead of manifests.
    /// </summary>
    public static IReadOnlyList<UpdateCatalogEntry> GetUpdatePath(
        Version from, Version to, IEnumerable<UpdateCatalogEntry> entries)
    {
        var entryList = entries.ToList();
        var path = new List<UpdateCatalogEntry>();
        var current = from;

        while (current < to)
        {
            UpdateCatalogEntry? cumulative = null;
            foreach (var e in entryList.Where(e => e.IsCumulative))
            {
                if (!Version.TryParse(e.TargetVersion, out var tv) || tv > to)
                    continue;
                if (!Version.TryParse(e.MinSourceVersion, out var minV))
                    continue;
                Version? maxV = null;
                if (e.MaxSourceVersion is not null && Version.TryParse(e.MaxSourceVersion, out var mv))
                    maxV = mv;
                if (!Contains(current, minV, maxV))
                    continue;
                if (cumulative is null || Version.Parse(e.TargetVersion) > Version.Parse(cumulative.TargetVersion))
                    cumulative = e;
            }

            if (cumulative is not null)
            {
                path.Add(cumulative);
                current = Version.Parse(cumulative.TargetVersion);
                continue;
            }

            UpdateCatalogEntry? next = null;
            foreach (var e in entryList)
            {
                if (!Version.TryParse(e.TargetVersion, out var tv) || tv > to || tv <= current)
                    continue;
                if (!Version.TryParse(e.MinSourceVersion, out var minV))
                    continue;
                Version? maxV = null;
                if (e.MaxSourceVersion is not null && Version.TryParse(e.MaxSourceVersion, out var mv))
                    maxV = mv;
                if (!Contains(current, minV, maxV))
                    continue;
                if (next is null || tv < Version.Parse(next.TargetVersion))
                    next = e;
            }

            if (next is null)
                break;

            path.Add(next);
            current = Version.Parse(next.TargetVersion);
        }

        if (current < to)
            return [];

        return path;
    }
}
