using Orbbec;

namespace Samples.ForceIP
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Force IP Configuration - Starting...");
            Console.WriteLine("This tool is used to configure network IP for Ethernet devices.");

            try
            {
                using var context = new Context();
                using var deviceList = context.QueryDeviceList();

                // Get Ethernet device list
                var ethernetDevices = new List<(uint Index, string Name, string SerialNumber, string MAC, string IP, string SubnetMask, string Gateway)>();

                Console.WriteLine("\nEthernet device list:");
                for (uint i = 0; i < deviceList.DeviceCount(); i++)
                {
                    var connectionType = deviceList.ConnectionType(i);
                    if (connectionType != "Ethernet")
                        continue;

                    var name = deviceList.Name(i);
                    var serialNumber = deviceList.SerialNumber(i);
                    var mac = deviceList.Uid(i);
                    var ip = deviceList.IPAddress(i);
                    var subnetMask = deviceList.GetSubnetMask(i);
                    var gateway = deviceList.GetGateway(i);

                    Console.WriteLine($"{ethernetDevices.Count}. Name: {name}, Serial Number: {serialNumber}");
                    Console.WriteLine($"   MAC: {mac}, IP: {ip}, Subnet Mask: {subnetMask}, Gateway: {gateway}");

                    ethernetDevices.Add((i, name, serialNumber, mac, ip, subnetMask, gateway));
                }

                if (ethernetDevices.Count == 0)
                {
                    Console.WriteLine("No network devices found.");
                    return;
                }

                // Select device
                uint selectedIndex;
                while (true)
                {
                    Console.WriteLine("\nEnter your choice: ");
                    var input = Console.ReadLine();
                    if (uint.TryParse(input, out selectedIndex) && selectedIndex < ethernetDevices.Count)
                    {
                        break;
                    }
                    Console.WriteLine("Invalid input, please enter a valid index number.");
                }

                var selectedDevice = ethernetDevices[(int)selectedIndex];

                // Get IP configuration from user
                var config = GetIPConfig();

                // Apply IP configuration
                bool result = Context.ForceIpConfig(selectedDevice.MAC, config);
                if (result)
                {
                    Console.WriteLine("The new IP configuration has been successfully applied to the device.");
                }
                else
                {
                    Console.WriteLine("Failed to apply the new IP configuration.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey(true);
        }

        static DeviceIpAddrConfig GetIPConfig()
        {
            DeviceIpAddrConfig config = new DeviceIpAddrConfig
            {
                address = new byte[4],
                mask = new byte[4],
                gateway = new byte[4]
            };

            Console.WriteLine("Please enter the network configuration information:");

            // Get IP address
            Console.WriteLine("Enter IP address:");
            while (true)
            {
                var input = Console.ReadLine();
                if (ParseIpString(input, config.address))
                {
                    break;
                }
                Console.WriteLine("Invalid format. Enter IP address:");
            }

            // Get Subnet Mask
            Console.WriteLine("Enter Subnet Mask:");
            while (true)
            {
                var input = Console.ReadLine();
                if (ParseIpString(input, config.mask))
                {
                    break;
                }
                Console.WriteLine("Invalid format. Enter Subnet Mask:");
            }

            // Get Gateway
            Console.WriteLine("Enter Gateway address:");
            while (true)
            {
                var input = Console.ReadLine();
                if (ParseIpString(input, config.gateway))
                {
                    break;
                }
                Console.WriteLine("Invalid format. Enter Gateway address:");
            }

            config.dhcp = 0; // Static IP
            return config;
        }

        static bool ParseIpString(string input, byte[] output)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            try
            {
                var parts = input.Split('.');
                if (parts.Length != 4)
                {
                    return false;
                }

                for (int i = 0; i < 4; i++)
                {
                    if (!byte.TryParse(parts[i], out byte value))
                    {
                        return false;
                    }
                    output[i] = value;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
