using System.Text.RegularExpressions;

namespace VIBN_Tools.Core.Interfaces;

/// <summary>PLC directions, independent of language, peripheral prefix and bit/word notation.</summary>
public static partial class PlcSignalSlotPolicy
{
    [GeneratedRegex(@"^%?P?([AEIQ])[XBWDL]?\d+(?:\.[0-7])?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

    public static string AssignmentWarning(string? address, string? slot)
    {
        var match = AddressPattern().Match((address ?? "").Trim());
        if (!match.Success) return "";
        var output = "AQ".Contains(char.ToUpperInvariant(match.Groups[1].Value[0]));
        var wrong = output ? (slot ?? "").StartsWith("PLC_IN_", StringComparison.OrdinalIgnoreCase)
            : (slot ?? "").StartsWith("PLC_OUT_", StringComparison.OrdinalIgnoreCase);
        return wrong ? $"{(output ? "Ausgangsadresse" : "Eingangsadresse")} „{address}“ ist einem " +
            $"{(output ? "Eingangsslot" : "Ausgangsslot")} „{slot}“ zugeordnet. Bitte die Zuordnung fachlich prüfen." : "";
    }
}
