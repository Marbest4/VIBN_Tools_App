namespace VIBN_Tools.ContainerToFeeVisual;

/// <summary>One currently readable slot-to-slot connection between two FEE scene objects.</summary>
public sealed record VisualFeeObjectLink(
    string ObjectGuidString,
    string SlotName,
    string LinkedObjectGuidString,
    string LinkedSlotName);


public sealed record FeeSignalEndpoint(Guid Guid, string Name, string FeeType, string LogicDefinitionName = "",
    IReadOnlyList<string>? SlotNames = null, string ParentGuid = "", string ParentName = "", string CabinetDefinition = "");

public sealed record FeeContainerLiveAssignment(
    Guid VariableGuid,
    Guid TargetObjectGuid,
    string TargetSlot,
    bool IsIndirect = false);


public sealed record FeeContainerLiveVariable(
    Guid VariableGuid,
    string Signal,
    string Address,
    string Path,
    string DataType,
    string Comment,
    string Usage = "", string InterfaceGuid = "", string InterfaceName = "");


public sealed record FeeContainerObjectDetails(Guid Guid, string Name, string FeeType,
    string ParentName, string LogicDefinitionName, string ConnectionText, bool IsSpecialDevice = false, string ParentGuid = "",
    IReadOnlyList<string>? SlotNames = null, string CabinetDefinition = "");


public sealed record FeeContainerObjectAssociation(
    Guid ObjectGuid,
    string ObjectName,
    string ObjectType,
    Guid ContainerObjectGuid,
    string ContainerId,
    string Reason,
    string Role = "SimObject",
    string ProvenanceContainerId = "",
    bool IsManual = false,
    string ParentName = "",
    string LogicDefinitionName = "",
    string ConnectionText = "");
