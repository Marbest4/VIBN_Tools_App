using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using VIBN_Tools.Tia.Contracts;

namespace VIBN_Tools.TiaBridge.Openness;

/// <summary>
/// Read-only adapter for the version-specific Siemens hardware object model.
/// It keeps every DeviceItem hierarchy node separate and never modifies the
/// attached TIA project.
/// </summary>
internal sealed class TiaHardwareReader
{
    private readonly Assembly _engineeringAssembly;

    public TiaHardwareReader(Assembly engineeringAssembly)
    {
        _engineeringAssembly = engineeringAssembly ?? throw new ArgumentNullException(nameof(engineeringAssembly));
    }

    public IReadOnlyList<TiaHardwareModuleInfo> Read(object project, int selectedDeviceIndex)
    {
        var devices = EnumerateDevices(project);
        var result = new List<TiaHardwareModuleInfo>();
        for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
        {
            var device = devices[deviceIndex];
            var context = ReadDeviceContext(device, deviceIndex);
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Traverse(
                device,
                context,
                result,
                identities,
                parentPath: string.Empty,
                depth: 0,
                parentSlot: -1,
                inheritedNetwork: ReadNetworkMetadata(device));
        }

        return result
            .OrderBy(module => module.DeviceIndex == selectedDeviceIndex ? 0 : 1)
            .ThenBy(module => module.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(module => module.ModulePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(module => module.Slot < 0 ? int.MaxValue : module.Slot)
            .ThenBy(module => module.Subslot < 0 ? int.MaxValue : module.Subslot)
            .ToArray();
    }

    /// <summary>
    /// Enumerates the complete TIA project device tree in a stable order:
    /// root devices, user groups (including nested groups), then the system
    /// group for ungrouped distributed devices. Root devices stay first so
    /// existing PLC indices remain stable.
    /// </summary>
    internal IReadOnlyList<object> EnumerateDevices(object project)
    {
        if (project is null)
            throw new ArgumentNullException(nameof(project));

        var devices = new List<object>();
        AddUniqueDevices(ReadEnumerableMember(project, "Devices"), devices);

        foreach (var group in ReadEnumerableMember(project, "DeviceGroups"))
            AddDeviceGroup(group, devices);

        var ungroupedDevices = ReadMember(project, "UngroupedDevicesGroup");
        if (ungroupedDevices is not null)
            AddUniqueDevices(ReadEnumerableMember(ungroupedDevices, "Devices"), devices);

        return devices;
    }

    private static void AddDeviceGroup(object group, ICollection<object> devices)
    {
        AddUniqueDevices(ReadEnumerableMember(group, "Devices"), devices);
        foreach (var childGroup in ReadEnumerableMember(group, "Groups"))
            AddDeviceGroup(childGroup, devices);
    }

    private static void AddUniqueDevices(IEnumerable<object> candidates, ICollection<object> devices)
    {
        foreach (var candidate in candidates)
        {
            if (!devices.Any(existing => ReferenceEquals(existing, candidate)))
                devices.Add(candidate);
        }
    }

    private void Traverse(
        object parent,
        DeviceContext device,
        ICollection<TiaHardwareModuleInfo> result,
        ISet<string> identities,
        string parentPath,
        int depth,
        int parentSlot,
        NetworkMetadata inheritedNetwork)
    {
        foreach (var item in ReadDeviceItems(parent))
        {
            var moduleName = ReadString(item, "Name");
            var position = ReadInt(item, "PositionNumber", "Slot");
            var slot = depth >= 2 ? parentSlot : position;
            var subslot = depth >= 2 ? position : -1;
            // Slot belongs to the module; deeper hierarchy levels represent
            // submodules and must retain their owning module slot.
            var nextParentSlot = depth >= 2 ? parentSlot : position;

            var modulePath = string.IsNullOrWhiteSpace(parentPath)
                ? moduleName
                : $"{parentPath}/{moduleName}";
            var typeIdentifier = ReadString(item, "TypeIdentifier");
            var moduleType = ReadString(item, "TypeName", "Classification");
            var deviceType = depth == 0 && moduleType.Length > 0
                ? moduleType
                : device.DeviceType;
            var manufacturer = FirstNotEmpty(
                ReadString(item, "Author", "Manufacturer"),
                device.Manufacturer);
            var orderNumber = FirstNotEmpty(
                ReadString(item, "OrderNumber"),
                ParseOrderNumber(typeIdentifier),
                device.OrderNumber);
            var firmware = FirstNotEmpty(
                ReadString(item, "FirmwareVersion"),
                ParseFirmware(typeIdentifier),
                device.FirmwareVersion);
            var gsd = ReadGsdMetadata(item, "Siemens.Engineering.HW.Features.GsdDeviceItem");
            if (gsd.IsEmpty)
                gsd = device.Gsd;
            var localNetwork = ReadNetworkMetadata(item);
            var network = MergeNetworkMetadata(inheritedNetwork, localNetwork);
            var addresses = ReadAddresses(item);

            var identity = $"{device.DeviceIndex}|{modulePath}|{slot}|{subslot}|{typeIdentifier}";
            if (identities.Add(identity))
            {
                result.Add(new TiaHardwareModuleInfo
                {
                    DeviceIndex = device.DeviceIndex,
                    DeviceName = device.DeviceName,
                    DeviceType = deviceType,
                    Manufacturer = manufacturer,
                    OrderNumber = orderNumber,
                    FirmwareVersion = firmware,
                    GsdName = gsd.Name,
                    GsdType = gsd.Type,
                    ProfinetName = network.ProfinetName,
                    IpAddress = network.IpAddress,
                    NetworkRole = network.Role,
                    Slot = slot,
                    Subslot = subslot,
                    ModuleName = moduleName,
                    ModulePath = modulePath,
                    ModuleType = moduleType,
                    TypeIdentifier = typeIdentifier,
                    InputStartByte = addresses.InputStart,
                    InputLength = addresses.InputLength,
                    OutputStartByte = addresses.OutputStart,
                    OutputLength = addresses.OutputLength
                });
            }

            Traverse(
                item,
                device.WithMetadata(
                    deviceType,
                    manufacturer,
                    orderNumber,
                    firmware,
                    gsd),
                result,
                identities,
                modulePath,
                depth + 1,
                nextParentSlot,
                network);
        }
    }

    private DeviceContext ReadDeviceContext(object device, int deviceIndex)
    {
        var typeIdentifier = ReadString(device, "TypeIdentifier");
        return new DeviceContext(
            deviceIndex,
            ReadString(device, "Name"),
            ReadString(device, "TypeName", "Classification"),
            ReadString(device, "Author", "Manufacturer"),
            FirstNotEmpty(ReadString(device, "OrderNumber"), ParseOrderNumber(typeIdentifier)),
            FirstNotEmpty(ReadString(device, "FirmwareVersion"), ParseFirmware(typeIdentifier)),
            ReadGsdMetadata(device, "Siemens.Engineering.HW.Features.GsdDevice"));
    }

    private AddressMetadata ReadAddresses(object item)
    {
        var inputStart = -1;
        var inputLength = 0;
        var outputStart = -1;
        var outputLength = 0;
        foreach (var address in ReadEnumerableMember(item, "Addresses"))
        {
            var ioType = ReadString(address, "IoType", "IOType");
            var start = ReadInt(address, "StartAddress", "StartAdress");
            var length = Math.Max(0, ReadInt(address, "Length"));
            if (ioType.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0)
                MergeRange(ref inputStart, ref inputLength, start, length);
            else if (ioType.IndexOf("Output", StringComparison.OrdinalIgnoreCase) >= 0)
                MergeRange(ref outputStart, ref outputLength, start, length);
        }
        return new AddressMetadata(inputStart, inputLength, outputStart, outputLength);
    }

    private NetworkMetadata ReadNetworkMetadata(object item)
    {
        var service = GetService(item, "Siemens.Engineering.HW.Features.NetworkInterface");
        if (service is null)
            return NetworkMetadata.Empty;

        var roles = new List<string>();
        if (ReadEnumerableMember(service, "IoControllers").Count > 0)
            roles.Add("IO-Controller");
        if (ReadEnumerableMember(service, "IoConnectors").Count > 0)
            roles.Add("IO-Device");

        foreach (var node in ReadEnumerableMember(service, "Nodes"))
        {
            var ipAddress = ReadString(node, "Address");
            var profinetName = ReadString(node, "PnDeviceName", "PnDeviceNameConverted");
            if (ipAddress.Length > 0 || profinetName.Length > 0)
                return new NetworkMetadata(profinetName, ipAddress, string.Join(" + ", roles));
        }
        return new NetworkMetadata(string.Empty, string.Empty, string.Join(" + ", roles));
    }

    private GsdMetadata ReadGsdMetadata(object target, string serviceTypeName)
    {
        var service = GetService(target, serviceTypeName);
        return new GsdMetadata(
            FirstNotEmpty(ReadString(service, "GsdName"), ReadString(target, "GsdName")),
            FirstNotEmpty(ReadString(service, "GsdType"), ReadString(target, "GsdType")));
    }

    private static NetworkMetadata MergeNetworkMetadata(
        NetworkMetadata inherited,
        NetworkMetadata local) => new(
            FirstNotEmpty(local.ProfinetName, inherited.ProfinetName),
            FirstNotEmpty(local.IpAddress, inherited.IpAddress),
            FirstNotEmpty(local.Role, inherited.Role));

    private object? GetService(object target, string serviceTypeName)
    {
        var serviceType = _engineeringAssembly.GetType(serviceTypeName, throwOnError: false);
        if (serviceType is null)
            return null;

        foreach (var method in EnumerateMethods(target.GetType(), "GetService"))
        {
            if (!method.IsGenericMethodDefinition || method.GetParameters().Length != 0)
                continue;
            try
            {
                return method.MakeGenericMethod(serviceType).Invoke(target, null);
            }
            catch (Exception)
            {
                // A DeviceItem provides only a subset of Openness services.
            }
        }
        return null;
    }

    private static IEnumerable<MethodInfo> EnumerateMethods(Type runtimeType, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (var type = runtimeType; type is not null; type = type.BaseType)
        {
            foreach (var method in type.GetMethods(flags).Where(method => method.Name == name))
                yield return method;
        }
        foreach (var interfaceType in runtimeType.GetInterfaces())
        {
            foreach (var method in interfaceType.GetMethods().Where(method => method.Name == name))
                yield return method;
        }
    }

    private static IReadOnlyList<object> ReadEnumerableMember(object? target, string name)
    {
        var value = ReadMember(target, name);
        return value is IEnumerable enumerable
            ? enumerable.Cast<object>().Where(item => item is not null).ToArray()
            : Array.Empty<object>();
    }

    private static IReadOnlyList<object> ReadDeviceItems(object parent)
    {
        var deviceItems = ReadEnumerableMember(parent, "DeviceItems");
        return deviceItems.Count > 0
            ? deviceItems
            : ReadEnumerableMember(parent, "Items");
    }

    private static string ReadString(object? target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(target, name);
            if (value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(value)))
                return Convert.ToString(value)!.Trim();
        }
        return string.Empty;
    }

    private static int ReadInt(object? target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(target, name);
            if (value is not null && int.TryParse(Convert.ToString(value), out var number))
                return number;
        }
        return -1;
    }

    private static object? ReadMember(object? target, string name)
    {
        if (target is null)
            return null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperties(flags).FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            try
            {
                if (property is not null)
                    return property.GetValue(target, null);
            }
            catch (Exception)
            {
                // Continue with explicit interface and dynamic attribute access.
            }
        }
        foreach (var interfaceType in target.GetType().GetInterfaces())
        {
            var property = interfaceType.GetProperties().FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            try
            {
                if (property is not null)
                    return property.GetValue(target, null);
            }
            catch (Exception)
            {
                // The runtime proxy may reject unsupported interface members.
            }
        }
        return ReadEngineeringAttribute(target, name);
    }

    private static object? ReadEngineeringAttribute(object target, string name)
    {
        try
        {
            var method = EnumerateMethods(target.GetType(), "GetAttribute")
                .FirstOrDefault(candidate =>
                    !candidate.IsGenericMethod &&
                    candidate.GetParameters().Length == 1 &&
                    candidate.GetParameters()[0].ParameterType == typeof(string));
            return method?.Invoke(target, new object[] { name });
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void MergeRange(ref int currentStart, ref int currentLength, int start, int length)
    {
        if (start < 0)
            return;
        if (currentStart < 0)
        {
            currentStart = start;
            currentLength = length;
            return;
        }
        var endExclusive = Math.Max(currentStart + currentLength, start + length);
        currentStart = Math.Min(currentStart, start);
        currentLength = Math.Max(0, endExclusive - currentStart);
    }

    private static string FirstNotEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string ParseOrderNumber(string typeIdentifier)
    {
        var match = Regex.Match(typeIdentifier ?? string.Empty, @"^OrderNumber:(?<value>[^/]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
    }

    private static string ParseFirmware(string typeIdentifier)
    {
        var match = Regex.Match(typeIdentifier ?? string.Empty, @"/(?<value>V[^/]+)$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
    }

    private sealed class DeviceContext
    {
        public DeviceContext(
            int deviceIndex,
            string deviceName,
            string deviceType,
            string manufacturer,
            string orderNumber,
            string firmwareVersion,
            GsdMetadata gsd)
        {
            DeviceIndex = deviceIndex;
            DeviceName = deviceName;
            DeviceType = deviceType;
            Manufacturer = manufacturer;
            OrderNumber = orderNumber;
            FirmwareVersion = firmwareVersion;
            Gsd = gsd;
        }

        public int DeviceIndex { get; }
        public string DeviceName { get; }
        public string DeviceType { get; }
        public string Manufacturer { get; }
        public string OrderNumber { get; }
        public string FirmwareVersion { get; }
        public GsdMetadata Gsd { get; }

        public DeviceContext WithMetadata(
            string deviceType,
            string manufacturer,
            string orderNumber,
            string firmwareVersion,
            GsdMetadata gsd) => new(
            DeviceIndex,
            DeviceName,
            deviceType,
            manufacturer,
            orderNumber,
            firmwareVersion,
            gsd);
    }

    private sealed class GsdMetadata
    {
        public GsdMetadata(string name, string type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }
        public string Type { get; }
        public bool IsEmpty => Name.Length == 0 && Type.Length == 0;
    }

    private sealed class NetworkMetadata
    {
        public NetworkMetadata(string profinetName, string ipAddress, string role)
        {
            ProfinetName = profinetName;
            IpAddress = ipAddress;
            Role = role;
        }

        public static NetworkMetadata Empty { get; } = new(string.Empty, string.Empty, string.Empty);
        public string ProfinetName { get; }
        public string IpAddress { get; }
        public string Role { get; }
        public bool IsEmpty => ProfinetName.Length == 0 && IpAddress.Length == 0 && Role.Length == 0;
    }

    private sealed class AddressMetadata
    {
        public AddressMetadata(int inputStart, int inputLength, int outputStart, int outputLength)
        {
            InputStart = inputStart;
            InputLength = inputLength;
            OutputStart = outputStart;
            OutputLength = outputLength;
        }

        public int InputStart { get; }
        public int InputLength { get; }
        public int OutputStart { get; }
        public int OutputLength { get; }
    }
}
