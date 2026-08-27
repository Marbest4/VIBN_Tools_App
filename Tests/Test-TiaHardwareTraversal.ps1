[CmdletBinding()]
param(
    [string]$BridgePath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BridgePath)) {
    $BridgePath = Join-Path $PSScriptRoot "..\VIBN_Tools.TiaBridge\bin\$Configuration\net48\VIBN_Tools.TiaBridge.exe"
}
$source = @'
using System.Collections.Generic;

public sealed class FakeTiaProject
{
    public FakeTiaProject()
    {
        Devices = new List<object>();
        DeviceGroups = new List<object>();
        UngroupedDevicesGroup = new FakeTiaGroup();
    }

    public List<object> Devices { get; private set; }
    public List<object> DeviceGroups { get; private set; }
    public FakeTiaGroup UngroupedDevicesGroup { get; private set; }
}

public sealed class FakeTiaGroup
{
    public FakeTiaGroup()
    {
        Devices = new List<object>();
        Groups = new List<object>();
    }

    public List<object> Devices { get; private set; }
    public List<object> Groups { get; private set; }
}

public sealed class FakeTiaDevice
{
    public FakeTiaDevice()
    {
        Name = string.Empty;
        TypeName = "Device";
        TypeIdentifier = string.Empty;
        DeviceItems = new List<object>();
        Items = new List<object>();
    }

    public string Name { get; set; }
    public string TypeName { get; set; }
    public string TypeIdentifier { get; set; }
    public List<object> DeviceItems { get; private set; }
    public List<object> Items { get; private set; }
}

public sealed class FakeTiaItem
{
    public FakeTiaItem()
    {
        Name = "Module";
        TypeName = "ModuleType";
        TypeIdentifier = string.Empty;
        DeviceItems = new List<object>();
        Addresses = new List<object>();
    }

    public string Name { get; set; }
    public string TypeName { get; set; }
    public string TypeIdentifier { get; set; }
    public int PositionNumber { get; set; }
    public List<object> DeviceItems { get; private set; }
    public List<object> Addresses { get; private set; }
}

public sealed class FakeTiaAddress
{
    public FakeTiaAddress()
    {
        IoType = "Input";
    }

    public string IoType { get; set; }
    public int StartAddress { get; set; }
    public int Length { get; set; }
}
'@

Add-Type -TypeDefinition $source -Language CSharp

function New-FakeDevice([string]$Name, [int]$InputAddress) {
    $device = [FakeTiaDevice]::new()
    $device.Name = $Name
    $item = [FakeTiaItem]::new()
    $item.Name = "$Name-Module"
    $item.PositionNumber = 1
    $address = [FakeTiaAddress]::new()
    $address.StartAddress = $InputAddress
    $address.Length = 4
    $item.Addresses.Add($address)
    $device.DeviceItems.Add($item)
    return $device
}

function New-FallbackDevice([string]$Name, [int]$OutputAddress) {
    $device = [FakeTiaDevice]::new()
    $device.Name = $Name
    $item = [FakeTiaItem]::new()
    $item.Name = "$Name-OutputModule"
    $item.PositionNumber = 2
    $address = [FakeTiaAddress]::new()
    $address.IoType = 'Output'
    $address.StartAddress = $OutputAddress
    $address.Length = 6
    $item.Addresses.Add($address)
    $device.Items.Add($item)
    return $device
}

$project = [FakeTiaProject]::new()
$project.Devices.Add((New-FakeDevice 'RootPLC' 0))
$group = [FakeTiaGroup]::new()
$group.Devices.Add((New-FakeDevice 'GroupedDevice' 10))
$subgroup = [FakeTiaGroup]::new()
$subgroup.Devices.Add((New-FakeDevice 'NestedDevice' 20))
$group.Groups.Add($subgroup)
$project.DeviceGroups.Add($group)
$project.UngroupedDevicesGroup.Devices.Add((New-FakeDevice 'UngroupedDevice' 30))
$project.UngroupedDevicesGroup.Devices.Add((New-FallbackDevice 'FallbackDevice' 40))

$resolvedBridge = (Resolve-Path -LiteralPath $BridgePath).Path
$bridge = [Reflection.Assembly]::LoadFrom($resolvedBridge)
$readerType = $bridge.GetType('VIBN_Tools.TiaBridge.Openness.TiaHardwareReader', $true)
$reader = [Activator]::CreateInstance($readerType, [object[]]@([FakeTiaProject].Assembly))
$rows = @($readerType.GetMethod('Read').Invoke($reader, [object[]]@($project, 0)))

if ($rows.Count -ne 5) {
    throw "Fünf Hardwarezeilen erwartet, aber $($rows.Count) erhalten."
}

$names = @($rows | ForEach-Object DeviceName)
foreach ($expected in @('RootPLC', 'GroupedDevice', 'NestedDevice', 'UngroupedDevice', 'FallbackDevice')) {
    if ($expected -notin $names) {
        throw "Gerät '$expected' fehlt in der Traversierung."
    }
}

$inputStarts = @($rows | Where-Object InputStartByte -ge 0 | ForEach-Object InputStartByte | Sort-Object)
if (($inputStarts -join ',') -ne '0,10,20,30') {
    throw "Unerwartete Eingangsadressen: $($inputStarts -join ',')"
}

$fallbackRow = $rows | Where-Object DeviceName -eq 'FallbackDevice' | Select-Object -First 1
if ($null -eq $fallbackRow -or $fallbackRow.OutputStartByte -ne 40 -or $fallbackRow.OutputLength -ne 6) {
    throw 'Items-Fallback oder Ausgangsadressen wurden nicht korrekt gelesen.'
}

Write-Host "TIA-Traversierung erfolgreich: $($names -join ', ')"
