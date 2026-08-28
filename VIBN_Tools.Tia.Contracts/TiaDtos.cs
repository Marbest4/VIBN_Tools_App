namespace VIBN_Tools.Tia.Contracts;

public sealed class EmptyPayload
{
    public static EmptyPayload Instance { get; } = new();

    private EmptyPayload()
    {
    }
}

public sealed class TiaVersionPayload
{
    public string Version { get; set; } = string.Empty;
}

public sealed class TiaPlcSelectionPayload
{
    public int PlcIndex { get; set; }
}

public sealed class TiaFolderPayload
{
    public string ParentPath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class TiaTransferPayload
{
    public string FolderPath { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;
}

public sealed class TiaPlcInfo
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    public string TypeIdentifier { get; set; } = string.Empty;
}

public sealed class TiaProjectTree
{
    public List<TiaFolderInfo> Folders { get; set; } = new();

    public List<TiaProgramItemInfo> Items { get; set; } = new();
}

public sealed class TiaFolderInfo
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}

public sealed class TiaProgramItemInfo
{
    public string Name { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;
}

public sealed class TiaAxisInfo
{
    public string Name { get; set; } = string.Empty;

    public string TechnologyType { get; set; } = string.Empty;
}

/// <summary>
/// Read-only hardware/module information discovered through TIA Openness.
/// Start addresses are byte offsets. Siemens Openness reports
/// <c>Address.Length</c> in bits, therefore the DTO keeps both the unmodified
/// bit length and the rounded-up byte length used by the UI and Special Device
/// generation. A negative start value means that the module has no address of
/// that IO type.
/// </summary>
public sealed class TiaHardwareModuleInfo
{
    public int DeviceIndex { get; set; }

    public int Slot { get; set; } = -1;

    public string DeviceName { get; set; } = string.Empty;

    public string DeviceType { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string GsdName { get; set; } = string.Empty;

    public string GsdType { get; set; } = string.Empty;

    public string ProfinetName { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string NetworkRole { get; set; } = string.Empty;

    public string ModuleName { get; set; } = string.Empty;

    public string ModulePath { get; set; } = string.Empty;

    public string ModuleType { get; set; } = string.Empty;

    public string TypeIdentifier { get; set; } = string.Empty;

    public string FirmwareVersion { get; set; } = string.Empty;

    public int Subslot { get; set; } = -1;

    /// <summary>
    /// Zero-based ordinal of the input/output range pair within one DeviceItem.
    /// It distinguishes multiple, separate address areas without merging them.
    /// </summary>
    public int AddressSetIndex { get; set; }

    public int InputStartByte { get; set; } = -1;

    /// <summary>Raw <c>Address.Length</c> value reported by Openness, in bits.</summary>
    public int InputLengthBits { get; set; }

    /// <summary>Input size rounded up to complete bytes.</summary>
    public int InputLength { get; set; }

    public int InputEndByte => InputStartByte >= 0 && InputLength > 0
        ? InputStartByte + InputLength - 1
        : -1;

    public string InputAddressRange => FormatAddressRange(InputStartByte, InputEndByte);

    public int OutputStartByte { get; set; } = -1;

    /// <summary>Raw <c>Address.Length</c> value reported by Openness, in bits.</summary>
    public int OutputLengthBits { get; set; }

    /// <summary>Output size rounded up to complete bytes.</summary>
    public int OutputLength { get; set; }

    public int OutputEndByte => OutputStartByte >= 0 && OutputLength > 0
        ? OutputStartByte + OutputLength - 1
        : -1;

    public string OutputAddressRange => FormatAddressRange(OutputStartByte, OutputEndByte);

    private static string FormatAddressRange(int start, int end) => start < 0
        ? "—"
        : end > start ? $"{start}–{end}" : start.ToString();
}
