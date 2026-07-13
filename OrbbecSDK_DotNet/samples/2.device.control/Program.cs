using System.Runtime.InteropServices;
using Orbbec;

namespace Samples.Control
{
    class Program
    {
        private static bool _shouldExit = false;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Control - Starting...");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _shouldExit = true;
            };

            Context? ctx = null;
            try
            {
                ctx = new Context();
                using var deviceList = ctx.QueryDeviceList();

                bool isSelectDevice = true;
                while (isSelectDevice && !_shouldExit)
                {
                    using var device = GetAvailableDevice(deviceList);
                    if (device == null) break;

                    using var deviceInfo = device.GetDeviceInfo();
                    Console.WriteLine("\n" + new string('-', 72));
                    Console.WriteLine("Current Device: \n" +
                                     $"name: {deviceInfo.Name()}" +
                                     $"vid: 0x{deviceInfo.Vid():X4}" +
                                     $"pid: 0x{deviceInfo.Pid():X4}" +
                                     $"uid: 0x{deviceInfo.Uid()}");

                    Console.WriteLine("Input \"?\" to get all properties, \"exit\" to exit and reselect device, \"quit\" to exit program.");

                    var propertyList = GetPropertyList(device);
                    Console.WriteLine(propertyList.Count());
                    propertyList.Sort((a, b) => a.id.CompareTo(b.id));

                    bool isSelectProperty = true;
                    while (isSelectProperty && !_shouldExit)
                    {
                        Console.Write("Select property (or 'exit' to go back): ");
                        string? choice = Console.ReadLine();
                        if (_shouldExit) break;

                        if (string.IsNullOrWhiteSpace(choice))
                            continue;

                        if (choice != "?")
                        {
                            string[] controlParts = choice.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                            if (controlParts.Length <= 0)
                                continue;

                            if (controlParts[0] == "exit")
                            {
                                isSelectProperty = false;
                                break;
                            }

                            if (controlParts[0] == "quit")
                            {
                                isSelectProperty = false;
                                isSelectDevice = false;
                                _shouldExit = true;
                                break;
                            }

                            if (controlParts.Length <= 1 || (controlParts[1] != "get" && controlParts[1] != "set") || controlParts.Length > 3
                                || (controlParts[1] == "set" && controlParts.Length < 3))
                            {
                                Console.WriteLine("Property control usage: [property index] [set] [property value] or [property index] [get]");
                                continue;
                            }

                            int size = propertyList.Count;
                            if (!int.TryParse(controlParts[0], out int selectId) || selectId >= size)
                            {
                                Console.WriteLine("Your selection is out of range, please reselect: ");
                                continue;
                            }

                            bool isGetValue = controlParts[1] == "get";
                            var propertyItem = propertyList[selectId];

                            if (isGetValue)
                            {
                                GetPropertyValue(device, propertyItem);
                            }
                            else
                            {
                                SetPropertyValue(device, propertyItem, controlParts[2]);
                            }
                        }
                        else
                        {
                            PrintPropertyList(device, propertyList);
                            Console.WriteLine("Please select property.(Property control usage: [property number] [set/get] [property value])");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                ctx?.Dispose();
                Console.WriteLine("Control sample exited.");
            }
        }

        private static Device? GetAvailableDevice(DeviceList deviceList)
        {
            if (deviceList.DeviceCount() == 0)
            {
                Console.WriteLine("Device Not Found");
                return null;
            }

            return deviceList.DeviceCount() == 1 ?
                deviceList.GetDevice(0) :
                SelectDevice(deviceList);
        }

        private static Device SelectDevice(DeviceList deviceList)
        {
            uint devCount = deviceList.DeviceCount();
            Console.WriteLine("\nDevice list: ");
            for (uint i = 0; i < devCount; i++)
            {
                Console.WriteLine($"{i}. name: {deviceList.Name(i)}, " +
                                 $"vid: 0x{deviceList.Vid(i):X4}, " +
                                 $"pid: 0x{deviceList.Pid(i):X4}, " +
                                 $"uid: 0x{deviceList.Uid(i)}, " +
                                 $"sn: {deviceList.SerialNumber(i)}");
            }
            Console.Write("Select a device: ");

            int devIndex;
            string? input = Console.ReadLine();
            while (!int.TryParse(input, out devIndex) || devIndex < 0 || devIndex >= devCount)
            {
                Console.WriteLine("Your select is out of range, please reselect: ");
                input = Console.ReadLine();
            }

            return deviceList.GetDevice((uint)devIndex);
        }

        private static List<PropertyItem> GetPropertyList(Device device)
        {
            var propertyList = new List<PropertyItem>();
            uint size = device.GetSupportedPropertyCount();
            for (uint i = 0; i < size; ++i)
            {
                var propertyItem = device.GetSupportedProperty(i);
                if (IsPrimaryTypeProperty(propertyItem) && propertyItem.permission != PermissionType.OB_PERMISSION_DENY)
                {
                    propertyList.Add(propertyItem);
                }
            }
            return propertyList;
        }

        private static bool IsPrimaryTypeProperty(PropertyItem propertyItem)
        {
            return propertyItem.type == PropertyType.OB_BOOL_PROPERTY ||
                propertyItem.type == PropertyType.OB_INT_PROPERTY ||
                propertyItem.type == PropertyType.OB_FLOAT_PROPERTY;
        }

        private static void GetPropertyValue(Device device, PropertyItem propertyItem)
        {
            var boolRet = false;
            var intRet = 0;
            var floatRet = 0.0f;
            var name = Marshal.PtrToStringAnsi(propertyItem.name);

            try
            {
                switch (propertyItem.type)
                {
                    case PropertyType.OB_BOOL_PROPERTY:
                        try
                        {
                            boolRet = device.GetBoolProperty(propertyItem.id);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"get bool property failed: {ex.Message}");
                        }
                        Console.WriteLine($"property name: {name}, get bool value: {boolRet}");
                        break;
                    case PropertyType.OB_INT_PROPERTY:
                        try
                        {
                            intRet = device.GetIntProperty(propertyItem.id);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"get int property failed: {ex.Message}");
                        }
                        Console.WriteLine($"property name: {name}, get int value: {intRet}");
                        break;
                    case PropertyType.OB_FLOAT_PROPERTY:
                        try
                        {
                            floatRet = device.GetFloatProperty(propertyItem.id);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"get float property failed: {ex.Message}");
                        }
                        Console.WriteLine($"property name: {name}, get float value: {floatRet}");
                        break;
                    default:
                        break;
                }
            }
            catch (Exception)
            {
                Console.WriteLine($"get property failed: {name}");
            }
        }

        private static void SetPropertyValue(Device device, PropertyItem propertyItem, string strValue)
        {
            var boolValue = 0;
            var intValue = 0;
            var floatValue = 0.0f;
            var name = Marshal.PtrToStringAnsi(propertyItem.name);

            try
            {
                switch (propertyItem.type)
                {
                    case PropertyType.OB_BOOL_PROPERTY:
                        try
                        {
                            if (int.TryParse(strValue, out boolValue))
                            {
                                device.SetBoolProperty(propertyItem.id, boolValue == 1);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"set bool property failed: {ex.Message}");
                        }
                        Console.WriteLine($"property name: {name}, set bool value: {boolValue}");
                        break;
                    case PropertyType.OB_INT_PROPERTY:
                        try
                        {
                            if (int.TryParse(strValue, out intValue))
                            {
                                device.SetIntProperty(propertyItem.id, intValue);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"set int property failed: {ex.Message}");
                        }
                        Console.WriteLine($"property name: {name}, set int value: {intValue}");
                        break;
                    case PropertyType.OB_FLOAT_PROPERTY:
                        try
                        {
                            if (float.TryParse(strValue, out floatValue))
                            {
                                device.SetFloatProperty(propertyItem.id, floatValue);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"set float property failed: {ex.Message}");
                        }
                        Console.WriteLine($"property name: {name}, set float value: {floatValue}");
                        break;
                    default:
                        break;
                }
            }
            catch (Exception)
            {
                Console.WriteLine($"set property failed: {name}");
            }
        }

        private static void PrintPropertyList(Device device, List<PropertyItem> propertyList)
        {
            Console.WriteLine($"size: {propertyList.Count}");
            if (propertyList.Count == 0)
            {
                Console.WriteLine("No supported property!");
                return;
            }

            Console.WriteLine("\n" + new string('-', 72));

            for (int i = 0; i < propertyList.Count; ++i)
            {
                var propertyItem = propertyList[i];
                var strRange = "";
                var name = Marshal.PtrToStringAnsi(propertyItem.name);

                switch (propertyItem.type)
                {
                    case PropertyType.OB_BOOL_PROPERTY:
                        strRange = "Bool value(min:0, max:1, step:1)";
                        break;
                    case PropertyType.OB_INT_PROPERTY:
                        try
                        {
                            var intRange = device.GetIntPropertyRange(propertyItem.id);
                            strRange = $"Int value(min:{intRange.min}, max:{intRange.max}, step:{intRange.step})";
                        }
                        catch (Exception)
                        {
                            Console.WriteLine("get int property range failed.");
                        }
                        break;
                    case PropertyType.OB_FLOAT_PROPERTY:
                        try
                        {
                            var floatRange = device.GetFloatPropertyRange(propertyItem.id);
                            strRange = $"Float value(min:{floatRange.min}, max:{floatRange.max}, step:{floatRange.step})";
                        }
                        catch (Exception)
                        {
                            Console.WriteLine("get float property range failed.");
                        }
                        break;
                    default:
                        break;
                }

                Console.WriteLine($"{i:D2}. {name}({(int)propertyItem.id}), permission={PermissionTypeToString(propertyItem.permission)}, range={strRange}");
            }

            Console.WriteLine(new string('-', 72));
        }

        private static string PermissionTypeToString(PermissionType permission)
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
                    return "_/_";
            }
        }
    }
}