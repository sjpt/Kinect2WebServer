using Orbbec;

namespace Samples.OptionalDepthPresetsUpdate
{
    class Program
    {
        private static bool _shouldExit = false;
        private static readonly List<Device> devices = new List<Device>{};

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Optional Depth Presets Update - Starting...");

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
                if (deviceList.DeviceCount() == 0)
                {
                    Console.WriteLine("No device found. Please connect a device first!");
                    return;
                }

                for (uint i = 0; i < deviceList.DeviceCount(); ++i)
                {
                    devices.Add(deviceList.GetDevice(i));
                }
                Console.WriteLine("Devices found:");
                PrintDeviceList();

                bool isSelectDevice = true;
                while (isSelectDevice && !_shouldExit)
                {
                    bool firstCall = true;
                    var updateState = UpgradeState.STAT_START;

                    isSelectDevice = SelectDevice(out Device? device);
                    if (!isSelectDevice || device == null)
                        break;

                    PrintPreset(device);

                    var pathList = GetPresetPath();
                    if (pathList == null)
                        break;

                    int index = 0;
                    int count = pathList.Count;
                    string[] filePaths = new string[count];

                    Console.WriteLine("\nPreset file paths you input: ");
                    foreach (var path in pathList)
                    {
                        filePaths[index] = path;
                        Console.WriteLine($"Path {index + 1}: {path}");
                        index++;
                    }
                    Console.WriteLine();

                    Console.WriteLine("Start to update optional depth preset, please wait a moment...\n");
                    try
                    {
                        device.UpdateOptionalDepthPresets(filePaths, count, (state, message, percent) =>
                        {
                            updateState = state;
                            PresetUpdateCallback(firstCall, state, message, percent);
                            firstCall = false;
                        });

                        Console.WriteLine();
                        if (updateState == UpgradeState.STAT_DONE || updateState == UpgradeState.STAT_DONE_WITH_DUPLICATES)
                        {
                            Console.WriteLine("After updating the preset: ");
                            PrintPreset(device);
                        }

                        if (!ShouldContinue())
                        {
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"\nThe update was interrupted! An error occurred! ");
                        Console.WriteLine($"Error message: {e.Message}\n");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                devices.ForEach(device => device?.Dispose());
                ctx?.Dispose();
                Console.WriteLine("OptionalDepthPresetsUpdate sample exited.");
            }
        }

        private static void PrintDeviceList()
        {
            Console.WriteLine(new string('-', 72));
            for (int i = 0; i < devices.Count; i++)
            {
                using var deviceInfo = devices[i].GetDeviceInfo();
                Console.WriteLine($"[{i}] Device: {deviceInfo.Name()} | SN: {deviceInfo.SerialNumber()} | Firmware version: {deviceInfo.FirmwareVersion()}");
            }
            Console.WriteLine(new string('-', 72));
        }

        private static bool SelectDevice(out Device? device)
        {
            device = null;
            while (true)
            {
                Console.WriteLine("Please select a device to update the optional depth preset, enter 'l' to list devices, or enter 'q' to quit: ");
                Console.Write("Device index: ");
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.Equals("q", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (input.Equals("l", StringComparison.OrdinalIgnoreCase))
                {
                    PrintDeviceList();
                    continue;
                }

                try
                {
                    if (!int.TryParse(input, out var index))
                        continue;

                    if (index < 0 || index >= devices.Count)
                    {
                        Console.WriteLine("Invalid input, please enter a valid index number.");
                        continue;
                    }

                    device = devices[index];
                    if (!IsPresetSupported(device))
                    {
                        Console.WriteLine("The device you selected does not support preset. Please select another one");
                        continue;
                    }
                    Console.WriteLine();
                    break;
                }
                catch (Exception)
                {
                    Console.WriteLine("Invalid input, please enter a valid index number.");
                    continue;
                }
            }
            return true;
        }

        private static bool IsPresetSupported(Device device)
        {
            using var presetList = device.GetAvailablePresetList();
            if (presetList != null && presetList.Count() > 0)
            {
                return true;
            }
            return false;
        }

        private static List<string>? GetPresetPath()
        {
            Console.WriteLine("Please input the file paths of the optional depth preset file (.bin):");
            Console.WriteLine(" - Press 'Enter' to finish this input");
            Console.WriteLine(" - Press 'Q' or 'q' to exit the program");

            var pathList = new List<string>();
            int count = 0;

            while (count < 10)
            {
                Console.Write("Enter Path: ");
                var input = Console.ReadLine();

                if (input?.Equals("q", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    if (pathList.Count == 0)
                    {
                        Console.WriteLine("You didn't input any file paths");
                        if (!ShouldContinue())
                        {
                            return null;
                        }
                        continue;
                    }
                    return pathList;
                }

                input = input.Trim().Trim('\'', '"');

                if (input.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    pathList.Add(input);
                    count++;
                }
                else
                {
                    Console.WriteLine("Invalid file format. Please provide a .bin file.\n");
                }
            }

            return pathList;
        }

        private static bool ShouldContinue()
        {
            Console.Write("Enter 'Q' or 'q' to quit, or any other key to continue: ");
            return !Console.ReadLine()?.Equals("q", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static void PrintPreset(Device device)
        {
            try
            {
                using var presetList = device.GetAvailablePresetList();
                Console.WriteLine($"Preset count: {presetList.Count()}");
                for (uint i = 0; i < presetList.Count(); ++i)
                {
                    Console.WriteLine($" - {presetList.GetName(i)}");
                }
                Console.WriteLine($"Current preset: {device.GetCurrentPresetName()}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine("\nThe device doesn't support preset! ");
                Console.WriteLine($"error: {ex.Message}\n");
            }

            string key = "PresetVer";
            if (device.IsExtensionInfoExist(key))
            {
                string value = device.GetExtensionInfo(key);
                Console.WriteLine($"Preset version: {value}\n");
            }
            else
            {
                Console.WriteLine("PresetVer: n/a\n");
            }
        }

        private static void PresetUpdateCallback(bool firstCall, UpgradeState state, string message, byte percent)
        {
            if (!firstCall)
            {
                Console.Write("\033[3F");
            }

            Console.Write("\033[K");
            Console.WriteLine($"Progress: {percent}%");

            Console.Write("\033[K");

            string statusText = state switch
            {
                UpgradeState.STAT_VERIFY_SUCCESS => "Image file verification success",
                UpgradeState.STAT_FILE_TRANSFER => "File transfer in progress",
                UpgradeState.STAT_DONE => "Update completed",
                UpgradeState.STAT_DONE_WITH_DUPLICATES => "Update completed, duplicated presets have been ignored",
                UpgradeState.STAT_IN_PROGRESS => "Upgrade in progress",
                UpgradeState.STAT_START => "Starting the upgrade",
                UpgradeState.STAT_VERIFY_IMAGE => "Verifying image file",
                _ => "Unknown status or error"
            };
            Console.WriteLine($"Status  : {statusText}");

            Console.Write("\033[K");
            Console.WriteLine($"Message : {message}");
            Console.Out.Flush();
        }
    }
}