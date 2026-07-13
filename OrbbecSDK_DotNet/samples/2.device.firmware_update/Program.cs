using Orbbec;

namespace Samples.FirmwareUpdate
{
    class Program
    {
        private static bool _shouldExit = false;
        private static readonly List<Device> devices = new List<Device>{};

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Firmware Update - Starting...");

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
                    int deviceIndex = -1;

                    isSelectDevice = SelectDevice(ref deviceIndex);
                    if (!isSelectDevice)
                        break;

                    string firmwarePath = "";
                    if (!GetFirmwarePath(ref firmwarePath))
                        break;

                    Console.WriteLine("Upgrading device firmware, please wait...\n");
                    try
                    {
                        devices[deviceIndex].DeviceUpgrade(firmwarePath, (state, message, percent) =>
                        {
                            FirmwareUpdateCallback(firstCall, state, message, percent);
                            firstCall = false;
                        }, false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("\nThe upgrade was interrupted! An error occurred! ");
                        Console.WriteLine($"Error message: {e.Message}\n");
                        break;
                    }

                    Console.WriteLine("Enter 'Q' or 'q' to quit, or any other key to continue: ");
                    var input = Console.ReadLine();
                    if (!string.IsNullOrEmpty(input) && input.Equals("q", StringComparison.OrdinalIgnoreCase))
                    {
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
                Console.WriteLine("FirmwareUpdate sample exited.");
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

        private static bool SelectDevice(ref int deviceIndex)
        {
            while (true)
            {
                Console.WriteLine("Please select a device to update the firmware, enter 'l' to list devices, or enter 'q' to quit: ");
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
                    if (int.TryParse(input, out deviceIndex))
                    {
                        if (deviceIndex < 0 || deviceIndex >= devices.Count)
                        {
                            Console.WriteLine("Invalid input, please enter a valid index number.");
                            continue;
                        }
                        Console.WriteLine();
                        break;
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("Invalid input, please enter a valid index number.");
                    continue;
                }
            }

            return true;
        }

        private static bool GetFirmwarePath(ref string firmwarePath)
        {
            while (true)
            {
                Console.WriteLine("Please input the path of the firmware file (.bin) to be updated:");
                Console.WriteLine("(Enter 'Q' or 'q' to quit): ");
                Console.Write("Path: ");
                string input = Console.ReadLine()?.Trim() ?? "";

                if (input.Equals("q", StringComparison.OrdinalIgnoreCase))
                    return false;

                input = input.Trim().Trim('\"', '\'', '`');

                string extension = Path.GetExtension(input).ToLower();
                if (extension == ".bin" || extension == ".img")
                {
                    firmwarePath = input;
                    Console.WriteLine($"Firmware file confirmed: {firmwarePath}\n");
                    break;
                }

                Console.WriteLine("Invalid file format. Please provide a .bin file.\n");
            }

            return true;
        }

        private static void FirmwareUpdateCallback(bool firstCall, UpgradeState state, string message, byte percent)
        {
            if (!firstCall)
            {
                Console.Write("\x1b[3F");
            }

            string statusText = state switch
            {
                UpgradeState.STAT_VERIFY_SUCCESS => "Image file verification success",
                UpgradeState.STAT_FILE_TRANSFER => "File transfer in progress",
                UpgradeState.STAT_DONE => "Update completed",
                UpgradeState.STAT_IN_PROGRESS => "Upgrade in progress",
                UpgradeState.STAT_START => "Starting the upgrade",
                UpgradeState.STAT_VERIFY_IMAGE => "Verifying image file",
                _ => "Unknown status or error"
            };

            Console.Write("\x1b[K");
            Console.WriteLine($"Progress: {(uint)percent}%");

            Console.Write("\x1b[K");
            Console.WriteLine($"Status  : {statusText}");

            Console.Write("\x1b[K");
            Console.WriteLine($"Message : {message}");
            Console.Out.Flush();
        }
    }
}