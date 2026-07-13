using Orbbec;

namespace Samples.Enumerate
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Device Enumerate - Starting...\n");

            try
            {
                using var context = new Context();

                while (true)
                {
                    // Query the list of connected devices
                    using var deviceList = context.QueryDeviceList();
                    if (deviceList.DeviceCount() < 1)
                    {
                        Console.WriteLine("No device found! Please connect a supported device and retry this program.");
                        Console.WriteLine("\nPress any key to exit.");
                        Console.ReadKey(true);
                        return;
                    }

                    Console.WriteLine("Enumerated devices: ");

                    for (uint index = 0; index < deviceList.DeviceCount(); index++)
                    {
                        using var device = deviceList.GetDevice(index);
                        using var deviceInfo = device.GetDeviceInfo();
                        Console.WriteLine($"  {index}- device name: {deviceInfo.Name()}, device pid: 0x{deviceInfo.Pid():X4}, device SN: {deviceInfo.SerialNumber()}, connection type: {deviceInfo.ConnectionType()}");
                    }

                    Console.WriteLine("Select a device to enumerate its sensors (Input device index or 'ESC' to exit program):");

                    // Select a device
                    int deviceSelected = GetInputOption();
                    if (deviceSelected >= (int)deviceList.DeviceCount() || deviceSelected < 0)
                    {
                        if (deviceSelected == -1)
                        {
                            break;
                        }
                        else
                        {
                            Console.WriteLine("\nInvalid input, please reselect the device!\n");
                            continue;
                        }
                    }

                    // Get the device
                    using var selectedDevice = deviceList.GetDevice((uint)deviceSelected);
                    EnumerateSensors(selectedDevice);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("\nPress any key to exit.");
                Console.ReadKey(true);
            }
        }

        // Get input option from user (number or ESC)
        static int GetInputOption()
        {
            var keyInfo = Console.ReadKey(true);
            if (keyInfo.Key == ConsoleKey.Escape)
            {
                return -1;
            }
            return keyInfo.KeyChar - '0';
        }

        // Enumerate sensors
        static void EnumerateSensors(Device device)
        {
            while (true)
            {
                Console.WriteLine("Sensor list: ");

                // Get the list of sensors
                using var sensorList = device.GetSensorList();
                for (uint index = 0; index < sensorList.SensorCount(); index++)
                {
                    // Get the sensor type
                    var sensorType = sensorList.SensorType(index);
                    Console.WriteLine($" - {index}. sensor type: {sensorType}");
                }

                Console.WriteLine("Select a sensor to enumerate its streams (input sensor index or 'ESC' to enumerate device):");

                // Select a sensor
                int sensorSelected = GetInputOption();
                if (sensorSelected >= (int)sensorList.SensorCount() || sensorSelected < 0)
                {
                    if (sensorSelected == -1)
                    {
                        break;
                    }
                    else
                    {
                        Console.WriteLine("\nInvalid input, please reselect the sensor!\n");
                        continue;
                    }
                }

                // Get sensor from sensorList
                using var sensor = sensorList.GetSensor((uint)sensorSelected);
                EnumerateStreamProfiles(sensor);
            }
        }

        // Enumerate stream profiles
        static void EnumerateStreamProfiles(Sensor sensor)
        {
            // Get the list of stream profiles
            using var streamProfileList = sensor.GetStreamProfileList();
            // Get the sensor type
            var sensorType = sensor.GetSensorType();

            for (uint index = 0; index < streamProfileList.ProfileCount(); index++)
            {
                // Get the stream profile
                using var profile = streamProfileList.GetProfile((int)index);
                if (IsVideoSensorType(sensorType))
                {
                    PrintStreamProfile(profile, index);
                }
                else if (sensorType == SensorType.OB_SENSOR_ACCEL)
                {
                    PrintAccelProfile(profile, index);
                }
                else if (sensorType == SensorType.OB_SENSOR_GYRO)
                {
                    PrintGyroProfile(profile, index);
                }
                else
                {
                    break;
                }
            }
        }

        // Check if sensor type is a video sensor
        static bool IsVideoSensorType(SensorType sensorType)
        {
            return sensorType == SensorType.OB_SENSOR_COLOR ||
                   sensorType == SensorType.OB_SENSOR_DEPTH ||
                   sensorType == SensorType.OB_SENSOR_IR ||
                   sensorType == SensorType.OB_SENSOR_IR_LEFT ||
                   sensorType == SensorType.OB_SENSOR_IR_RIGHT ||
                   sensorType == SensorType.OB_SENSOR_COLOR_LEFT ||
                   sensorType == SensorType.OB_SENSOR_COLOR_RIGHT ||
                   sensorType == SensorType.OB_SENSOR_CONFIDENCE;
        }

        // Print stream profile information
        static void PrintStreamProfile(StreamProfile profile, uint index)
        {
            // Get the video profile
            using var videoProfile = profile.As<VideoStreamProfile>();
            if (videoProfile == null)
                return;

            // Get the format
            var formatName = profile.GetFormat();
            // Get the width
            var width = videoProfile.GetWidth();
            // Get the height
            var height = videoProfile.GetHeight();
            // Get the fps
            var fps = videoProfile.GetFPS();

            Console.WriteLine($"  {index}. format: {formatName}, res: {width}*{height}, fps: {fps}");
        }

        // Print accel profile information
        static void PrintAccelProfile(StreamProfile profile, uint index)
        {
            // Get the profile of accel
            using var accProfile = profile.As<AccelStreamProfile>();
            // Get the rate of accel
            var accRate = accProfile.GetSampleRate();
            Console.WriteLine($"  {index}. acc rate: {accRate}");
        }

        // Print gyro profile information
        static void PrintGyroProfile(StreamProfile profile, uint index)
        {
            // Get the profile of gyro
            using var gyroProfile = profile.As<GyroStreamProfile>();
            // Get the rate of gyro
            var gyroRate = gyroProfile.GetSampleRate();
            Console.WriteLine($"  {index}. gyro rate: {gyroRate}");
        }
    }
}
