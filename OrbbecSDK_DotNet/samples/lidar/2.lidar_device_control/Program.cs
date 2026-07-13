using Orbbec;
using System.Runtime.InteropServices;
using System.Text;

namespace LiDAR.DeviceControl
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("LiDAR Device Control - Starting...");

            try
            {
                using var context = new Context();
                using var deviceList = context.QueryDeviceList();

                if (deviceList.DeviceCount() < 1)
                {
                    Console.WriteLine("Device Not Found");
                    return;
                }

                using var device = deviceList.DeviceCount() == 1
                    ? deviceList.GetDevice(0)
                    : SelectDevice(deviceList);

                if (!IsLiDARDevice(device))
                {
                    Console.WriteLine("Invalid device, please connect a LiDAR device!");
                    return;
                }

                // Print device info (aligned with SDK)
                using var info = device.GetDeviceInfo();
                Console.WriteLine("\n------------------------------------------------------------------------");
                Console.WriteLine($"Current Device: name: {info.Name()}, vid: 0x{info.Vid():X4}, pid: 0x{info.Pid():X4}, uid: 0x{info.Uid()}");

                // Get property list and sort by ID (aligned with SDK)
                var propertyList = GetPropertyList(device);
                propertyList.Sort((a, b) => a.Id.CompareTo(b.Id));

                Console.WriteLine("\nInput \"?\" to get all properties.");
                Console.WriteLine("Input \"exit\" to exit the program.");

                bool isSelectProperty = true;
                while (isSelectProperty)
                {
                    Console.Write("\n> ");
                    string? choice = Console.ReadLine();

                    if (string.IsNullOrEmpty(choice)) continue;

                    if (choice == "?")
                    {
                        PrintPropertyList(device, propertyList);
                        Console.WriteLine("Please select property.(Property control usage: [property number] [set/get] [property value])");
                    }
                    else if (choice == "exit")
                    {
                        isSelectProperty = false;
                        break;
                    }
                    else
                    {
                        // Parse command like C++ SDK: [index] [set/get] [value]
                        var parts = choice.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2 || (parts[1] != "get" && parts[1] != "set") || parts.Length > 3
                           || (parts[1] == "set" && parts.Length < 3))
                        {
                            Console.WriteLine("Property control usage: [property index] [set] [property value] or [property index] [get]");
                            continue;
                        }

                        if (!int.TryParse(parts[0], out int selectId) || selectId < 0 || selectId >= propertyList.Count)
                        {
                            Console.WriteLine("Your selection is out of range, please reselect:");
                            continue;
                        }

                        var propertyItem = propertyList[selectId];
                        if (parts[1] == "get")
                        {
                            GetPropertyValue(device, propertyItem);
                        }
                        else
                        {
                            SetPropertyValue(device, propertyItem, parts[2]);
                        }
                    }
                }

                Console.WriteLine("LiDAR Device Control exited.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static string GetPropertyName(PropertyId id)
        {
            // Return readable name based on PropertyId
            return id.ToString().Replace("OB_PROP_", "").Replace("_INT", "").Replace("_BOOL", "").Replace("_FLOAT", "");
        }

        static void GetPropertyValue(Device device, PropertyItemInfo property)
        {
            try
            {
                switch (property.Type)
                {
                    case PropertyType.OB_BOOL_PROPERTY:
                        bool boolVal = device.GetBoolProperty(property.Id);
                        Console.WriteLine($"property name:{property.Name},get bool value:{boolVal}");
                        break;
                    case PropertyType.OB_INT_PROPERTY:
                        int intVal = device.GetIntProperty(property.Id);
                        Console.WriteLine($"property name:{property.Name},get int value:{intVal}");
                        break;
                    case PropertyType.OB_FLOAT_PROPERTY:
                        float floatVal = device.GetFloatProperty(property.Id);
                        Console.WriteLine($"property name:{property.Name},get float value:{floatVal}");
                        break;
                    default:
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"get property failed: {e.Message}");
            }
        }

        static void SetPropertyValue(Device device, PropertyItemInfo property, string valueStr)
        {
            try
            {
                switch (property.Type)
                {
                    case PropertyType.OB_BOOL_PROPERTY:
                        int boolVal = int.Parse(valueStr);
                        device.SetBoolProperty(property.Id, boolVal != 0);
                        Console.WriteLine($"property name:{property.Name},set bool value:{boolVal}");
                        break;
                    case PropertyType.OB_INT_PROPERTY:
                        int intVal = int.Parse(valueStr);
                        device.SetIntProperty(property.Id, intVal);
                        Console.WriteLine($"property name:{property.Name},set int value:{intVal}");
                        break;
                    case PropertyType.OB_FLOAT_PROPERTY:
                        float floatVal = float.Parse(valueStr);
                        device.SetFloatProperty(property.Id, floatVal);
                        Console.WriteLine($"property name:{property.Name},set float value:{floatVal}");
                        break;
                    default:
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"set property failed: {e.Message}");
            }
        }

        static void PrintPropertyList(Device device, List<PropertyItemInfo> properties)
        {
            Console.WriteLine($"size: {properties.Count}");
            if (properties.Count == 0)
            {
                Console.WriteLine("No supported property!");
            }

            Console.WriteLine("\n------------------------------------------------------------------------");
            for (int i = 0; i < properties.Count; i++)
            {
                var p = properties[i];
                string strRange = "";
                string permissionStr = PermissionTypeToString(p.Permission);

                switch (p.Type)
                {
                    case PropertyType.OB_BOOL_PROPERTY:
                        strRange = "Bool value(min:0, max:1, step:1)";
                        break;
                    case PropertyType.OB_INT_PROPERTY:
                        if ((p.Permission & PermissionType.OB_PERMISSION_READ) != 0)
                        {
                            try
                            {
                                var range = device.GetIntPropertyRange(p.Id);
                                strRange = $"Int value(min:{range.min}, max:{range.max}, step:{range.step})";
                            }
                            catch
                            {
                                strRange = "Int value";
                            }
                        }
                        else
                        {
                            strRange = "Int value";
                        }
                        break;
                    case PropertyType.OB_FLOAT_PROPERTY:
                        try
                        {
                            var range = device.GetFloatPropertyRange(p.Id);
                            strRange = $"Float value(min:{range.min}, max:{range.max}, step:{range.step})";
                        }
                        catch
                        {
                            strRange = "Float value";
                        }
                        break;
                    default:
                        break;
                }

                Console.WriteLine($"{i,2:D2}. {p.Name}({(int)p.Id}), permission={permissionStr}, range={strRange}");
            }
            Console.WriteLine("------------------------------------------------------------------------");
        }

        static string PermissionTypeToString(PermissionType permission)
        {
            switch (permission)
            {
                case PermissionType.OB_PERMISSION_READ:
                    return "R/_";
                case PermissionType.OB_PERMISSION_WRITE:
                    return "_/W";
                case PermissionType.OB_PERMISSION_READ_WRITE:
                    return "R/W";
                default:
                    break;
            }
            return "_/_";
        }

        static List<PropertyItemInfo> GetPropertyList(Device device)
        {
            var result = new List<PropertyItemInfo>();
            uint count = device.GetSupportedPropertyCount();

            for (uint i = 0; i < count; i++)
            {
                var item = device.GetSupportedProperty(i);
                if (IsPrimaryTypeProperty(item) && item.permission != PermissionType.OB_PERMISSION_DENY)
                {
                    // Convert IntPtr name to string
                    string nameStr = Marshal.PtrToStringAnsi(item.name) ?? GetPropertyName(item.id);

                    result.Add(new PropertyItemInfo
                    {
                        Id = item.id,
                        Name = nameStr,
                        Type = item.type,
                        Permission = item.permission
                    });
                }
            }

            return result;
        }

        static bool IsPrimaryTypeProperty(PropertyItem item)
        {
            return item.type == PropertyType.OB_INT_PROPERTY ||
                   item.type == PropertyType.OB_FLOAT_PROPERTY ||
                   item.type == PropertyType.OB_BOOL_PROPERTY;
        }

        static Device SelectDevice(DeviceList deviceList)
        {
            Console.WriteLine("Device list:");
            for (uint i = 0; i < deviceList.DeviceCount(); ++i)
            {
                Console.WriteLine($"{i}. name: {deviceList.Name(i)}, vid: 0x{deviceList.Vid(i):X4}, pid: 0x{deviceList.Pid(i):X4}, uid: 0x{deviceList.Uid(i)}, sn: {deviceList.SerialNumber(i)}");
            }
            Console.Write("Select a device: ");
            int idx = int.Parse(Console.ReadLine() ?? "0");
            while (idx < 0 || idx >= deviceList.DeviceCount())
            {
                Console.WriteLine("Your select is out of range, please reselect:");
                idx = int.Parse(Console.ReadLine() ?? "0");
            }
            return deviceList.GetDevice((uint)idx);
        }

        static bool IsLiDARDevice(Device device)
        {
            using var info = device.GetDeviceInfo();
            return info.Name().Contains("LiDAR", StringComparison.OrdinalIgnoreCase);
        }

        class PropertyItemInfo
        {
            public PropertyId Id { get; set; }
            public string Name { get; set; } = "";
            public PropertyType Type { get; set; }
            public PermissionType Permission { get; set; }
        }
    }
}