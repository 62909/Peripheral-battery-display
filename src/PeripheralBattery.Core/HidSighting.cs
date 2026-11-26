namespace PeripheralBattery.Core;

/// <summary>
/// One present HID interface. The name is the device's own product string.
/// A sighting is not a battery reading: it has no percentage and no model name.
/// </summary>
public readonly record struct HidInterfaceSighting(
    int? VendorId,
    int? ProductId,
    string? ReportedName,
    int? UsagePage,
    int? Usage);

/// <summary>
/// Text for the interfaces a refresh actually enumerated.
/// A sighting is a name and VID/PID. It is not a battery reading.
/// </summary>
public static class HidSightingText
{
    public static string Format(IReadOnlyList<HidInterfaceSighting> sightings)
    {
        var lines = Describe(sightings);
        if (lines.Count == 0)
            return "这次刷新没有看到 HID 接口。";
        return "这次刷新看到的 HID：\n" + string.Join("\n", lines);
    }

    public static IReadOnlyList<string> Describe(IReadOnlyList<HidInterfaceSighting> sightings)
    {
        var groups = new Dictionary<(int Vid, int Pid), Group>();
        foreach (var sighting in sightings)
        {
            if (sighting.VendorId is not int vid || sighting.ProductId is not int pid)
                continue;
            var key = (vid, pid);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new Group();
                groups.Add(key, group);
            }

            if (string.IsNullOrWhiteSpace(group.Name) && ReportableName(sighting.ReportedName) is string name)
                group.Name = name;
            if (sighting.UsagePage is int page && sighting.Usage is int usage)
                group.Usages.Add((page, usage));
        }

        var lines = new List<string>(groups.Count);
        foreach (var pair in groups.OrderBy(pair => pair.Key.Vid).ThenBy(pair => pair.Key.Pid))
            lines.Add(Line(pair.Key.Vid, pair.Key.Pid, pair.Value));
        return lines;
    }

    private static string Line(int vid, int pid, Group group)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(group.Name))
            parts.Add(group.Name);
        parts.Add(vid.ToString("X4") + ":" + pid.ToString("X4"));
        if (pid == AkkoHuaxiMouse.ProductId && vid == AkkoHuaxiMouse.VendorId && group.Usages.Count > 0)
        {
            parts.Add(string.Join(
                "、",
                group.Usages
                    .OrderBy(item => item.Page)
                    .ThenBy(item => item.Usage)
                    .Select(item => item.Page.ToString("X4") + ":" + item.Usage.ToString("X4"))));
        }

        return string.Join(" · ", parts);
    }

    private static string? ReportableName(string? reported)
    {
        if (string.IsNullOrWhiteSpace(reported))
            return null;
        return reported.Trim();
    }

    private sealed class Group
    {
        public string? Name { get; set; }
        public HashSet<(int Page, int Usage)> Usages { get; } = [];
    }
}
