namespace VIBN_Tools.ContainerToFeeVisual;

public static class FeeCabinetDefinitionType
{
    public static string? Resolve(string? definition)
    {
        var normalized = new string((definition ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (normalized.Contains("GROBNOTAUS", StringComparison.Ordinal))
            return "EStop";
        if (normalized.Contains("FUSE", StringComparison.Ordinal))
            return "CabinetFuse";
        if (normalized.Contains("LAMP", StringComparison.Ordinal))
            return "CabinetLamp";
        if (normalized.Contains("GROB2POSITIONSWITCH", StringComparison.Ordinal) ||
            normalized.Contains("GROB3POSITIONSWITCH", StringComparison.Ordinal) ||
            normalized.Contains("POSITIONSWITCH2", StringComparison.Ordinal) ||
            normalized.Contains("2POSITIONSWITCH", StringComparison.Ordinal) ||
            normalized.Contains("TWOPOSITIONSWITCH", StringComparison.Ordinal) ||
            normalized == "CABINETSWITCH")
            return "CabinetSwitch";
        if (normalized.Contains("BUTTON", StringComparison.Ordinal)) return "CabinetButton";
        if (normalized.Contains("MAINSWITCH", StringComparison.Ordinal)) return "CabinetMainSwitch";
        if (normalized.Contains("POSITIONSWITCH", StringComparison.Ordinal) || normalized.EndsWith("SWITCH", StringComparison.Ordinal)) return "CabinetSwitch";
        var actual = definition?.Trim().Replace('/', '\\').Split('\\').Last();
        if (string.IsNullOrWhiteSpace(actual) || Guid.TryParse(actual, out _)) return null;
        return actual.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? actual[..^4] : actual;
    }
}
