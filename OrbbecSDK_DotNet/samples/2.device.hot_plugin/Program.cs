using Orbbec;

namespace Samples.HotPlugin
{
    class Program
    {
        private static volatile bool _shouldExit = false;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Hot Plugin - Starting...");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _shouldExit = true;
            };

            Context? ctx = null;
            try
            {
                ctx = new Context();
                ctx.SetDeviceChangedCallback((removedList, deviceList) =>
                {
                    PrintDeviceList("added", deviceList);
                    PrintDeviceList("removed", removedList);
                });

                using var currentList = ctx.QueryDeviceList();
                PrintDeviceList("connected", currentList);
                currentList.Dispose();

                Console.WriteLine("Press 'r' to reboot the connected devices to trigger the device disconnect and reconnect event, or manually unplug and plugin the device.");
                Console.WriteLine("Press 'Esc' to exit.\n");

                while (!_shouldExit)
                {
                    if (Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(intercept: true);

                        if (keyInfo.Key == ConsoleKey.Escape)
                            break;

                        if (keyInfo.Key == ConsoleKey.R)
                        {
                            using (var rebootList = ctx.QueryDeviceList())
                            {
                                Console.WriteLine("Rebooting devices...");
                                RebootDevices(rebootList);
                            }
                        }
                    }

                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                ctx?.Dispose();
                Console.WriteLine("HotPlugin sample exited.");
            }
        }

        private static void PrintDeviceList(string prompt, DeviceList deviceList)
        {
            var count = deviceList.DeviceCount();
            if (count == 0) return;

            Console.WriteLine($"{count} device(s) {prompt}: ");
            for (uint i = 0; i < count; i++)
            {
                var uid = deviceList.Uid(i);
                var vid = deviceList.Vid(i);
                var pid = deviceList.Pid(i);
                var serialNumber = deviceList.SerialNumber(i);
                var connection = deviceList.ConnectionType(i);

                Console.WriteLine($" - uid: {uid}, vid: 0x{vid:X4}, pid: 0x{pid:X4}, serial number: {serialNumber}, connection: {connection}");
            }
            Console.WriteLine();
        }

        private static void RebootDevices(DeviceList deviceList)
        {
            for (int i = 0; i < deviceList.DeviceCount(); ++i)
            {
                using var device = deviceList.GetDevice((uint)i);
                device.Reboot();
            }
        }
    }
}