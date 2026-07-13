using Orbbec;
using System.Runtime.InteropServices;
using System.Text;

namespace LiDAR.Stream
{
    class Program
    {
        static uint frameCount = 0;
        static volatile bool isRunning = true;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("LiDAR Stream - Starting...");

            try
            {
                var context = new Context();
                using var deviceList = context.QueryDeviceList();

                if (deviceList.DeviceCount() < 1)
                {
                    Console.WriteLine("Device Not Found");
                    return;
                }

                Device device;
                if (deviceList.DeviceCount() == 1)
                {
                    device = deviceList.GetDevice(0);
                }
                else
                {
                    device = SelectDevice(deviceList);
                }

                if (!IsLiDARDevice(device))
                {
                    Console.WriteLine("Invalid device, please connect a LiDAR device!");
                    return;
                }

                // Print device info (aligned with SDK)
                using var info = device.GetDeviceInfo();
                Console.WriteLine("\n------------------------------------------------------------------------");
                Console.WriteLine($"Current Device: name: {info.Name()}, vid: 0x{info.Vid():X4}, pid: 0x{info.Pid():X4}, uid: 0x{info.Uid()}, sn: {info.SerialNumber()}");

                // Get and print LiDAR IP address (aligned with SDK - use 32 byte buffer)
                try
                {
                    byte[] ipData = new byte[32];
                    uint dataSize = 32;
                    device.GetStructuredData(PropertyId.OB_RAW_DATA_LIDAR_IP_ADDRESS, ipData, ref dataSize);
                    string ipStr = $"{ipData[3]}.{ipData[2]}.{ipData[1]}.{ipData[0]}";
                    Console.WriteLine($"LiDAR IP Address: {ipStr}");
                }
                catch
                {
                    Console.WriteLine("LiDAR IP Address: N/A");
                }

                var pipe = new Pipeline(device);
                using var config = new Config();

                // Set tail filter level to 0 (disable)
                device.SetIntProperty(PropertyId.OB_PROP_LIDAR_TAIL_FILTER_LEVEL_INT, 0);

                // Select and enable desired streams
                SelectStreams(device, config);

                // Set frame aggregate output mode
                config.SetFrameAggregateOutputMode(FrameAggregateOutputMode.OB_FRAME_AGGREGATE_OUTPUT_ALL_TYPE_FRAME_REQUIRE);

                // Start pipeline with callback
                pipe.Start(config, OnFrameSet);

                Console.WriteLine("\nThe stream is started!");
                Console.WriteLine("Press ESC to exit!\n");

                // Wait for exit
                while (isRunning)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Escape)
                        {
                            isRunning = false;
                            break;
                        }
                    }
                    Thread.Sleep(10);
                }

                pipe.Stop();
                device.Dispose();
                context.Dispose();
                Console.WriteLine("LiDAR Stream exited.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static void SelectStreams(Device device, Config config)
        {
            var selectedSensors = SelectSensors(device);
            if (selectedSensors.Count == 0)
            {
                Console.WriteLine("No sensor selected");
                return;
            }

            foreach (var sensor in selectedSensors)
            {
                using var profileList = sensor.GetStreamProfileList();
                if (profileList.ProfileCount() == 0)
                {
                    Console.WriteLine($"No stream profile found for sensor: {sensor.GetSensorType()}");
                    continue;
                }

                var sensorType = sensor.GetSensorType();
                Console.WriteLine($"Stream profile list for sensor: {sensorType}");
                StreamProfile? selectedProfile = null;
                for (uint i = 0; i < profileList.ProfileCount(); i++)
                {
                    using var profile = profileList.GetProfile((int)i);
                    var format = profile.GetFormat();

                    // Print detailed profile info based on sensor type (aligned with C++ SDK)
                    if (sensorType == SensorType.OB_SENSOR_ACCEL)
                    {
                        var accelProfile = profile.As<AccelStreamProfile>();
                        var accRate = accelProfile.GetSampleRate();
                        Console.WriteLine($" - {i}. acc rate: {accRate}");
                    }
                    else if (sensorType == SensorType.OB_SENSOR_GYRO)
                    {
                        var gyroProfile = profile.As<GyroStreamProfile>();
                        var gyroRate = gyroProfile.GetSampleRate();
                        Console.WriteLine($" - {i}. gyro rate: {gyroRate}");
                    }
                    else if (sensorType == SensorType.OB_SENSOR_LIDAR)
                    {
                        var lidarProfile = profile.As<LiDARStreamProfile>();
                        var scanRate = lidarProfile.GetScanRate();
                        Console.WriteLine($" - {i}. format: {format}, scan rate: {scanRate}");
                    }
                    else
                    {
                        Console.WriteLine($" - {i}. format: {format}");
                    }
                }

                Console.WriteLine("Select a stream profile to enable (input stream profile index): ");
                int selected = GetInputOption();
                if (selected >= 0 && selected < (int)profileList.ProfileCount())
                {
                    using var profile = profileList.GetProfile(selected);
                    selectedProfile = profile;
                    config.EnableStream(selectedProfile);
                }

                // Dispose sensor after use
                sensor.Dispose();
            }
        }

        static List<Sensor> SelectSensors(Device device)
        {
            var selectedSensors = new List<Sensor>();

            while (true)
            {
                Console.WriteLine("Sensor list:");
                using var sensorList = device.GetSensorList();
                for (uint i = 0; i < sensorList.SensorCount(); i++)
                {
                    var sensorType = sensorList.SensorType(i);
                    Console.WriteLine($" - {i}. sensor type: {sensorType}");
                }
                Console.WriteLine($" - {sensorList.SensorCount()}. all sensors");

                Console.WriteLine("Select a sensor to enable (input sensor index, or last number for all sensors): ");

                int sensorSelected = GetInputOption();
                if (sensorSelected < 0 || sensorSelected > sensorList.SensorCount())
                {
                    Console.WriteLine("\nInvalid input, please reselect the sensor!\n");
                    continue;
                }

                if (sensorSelected == sensorList.SensorCount())
                {
                    for (uint i = 0; i < sensorList.SensorCount(); i++)
                    {
                        selectedSensors.Add(sensorList.GetSensor(i));
                    }
                }
                else if (sensorSelected >= 0 && sensorSelected < (int)sensorList.SensorCount())
                {
                    selectedSensors.Add(sensorList.GetSensor((uint)sensorSelected));
                }
                break;
            }

            return selectedSensors;
        }

        static int GetInputOption()
        {
            string? input = Console.ReadLine();
            if (int.TryParse(input, out int result))
            {
                return result;
            }
            return -1;
        }

        static void OnFrameSet(Frameset frameset)
        {
            if (frameset == null) return;

            try
            {
                // Process frames using getCount() and getFrame(index) like SDK
                for (uint i = 0; i < frameset.GetFrameCount(); i++)
                {
                    using var frame = frameset.GetFrameByIndex((int)i);
                    if (frame == null) continue;

                    // Print frame information every 50 frames
                    if (frameCount % 50 == 0)
                    {
                        var type = frame.GetFrameType();
                        if (type == FrameType.OB_FRAME_LIDAR_POINTS)
                        {
                            PrintLiDARPointCloudInfo(frame);
                        }
                        else if (type == FrameType.OB_FRAME_ACCEL)
                        {
                            using var accelFrame = frame.As<AccelFrame>();
                            PrintImuValue(accelFrame, "m/s^2");
                        }
                        else if (type == FrameType.OB_FRAME_GYRO)
                        {
                            using var gyroFrame = frame.As<GyroFrame>();
                            PrintImuValue(gyroFrame, "rad/s");
                        }
                    }
                }
                frameCount++;
            }
            finally
            {
                frameset.Dispose();
            }
        }

        static void PrintLiDARPointCloudInfo(Frame frame)
        {
            var format = frame.GetFormat();
            uint dataSize = frame.GetDataSize();
            uint pointCount = dataSize / 16; // OB_FORMAT_LIDAR_POINT size

            Console.WriteLine($"frame index: {frame.GetIndex()}");
            Console.WriteLine($"LiDAR PointCloud Frame: ");
            Console.WriteLine($"{{");
            Console.WriteLine($"  tsp = {frame.GetTimeStampUs()}");
            Console.WriteLine($"  format = {format}");
            Console.WriteLine($"  valid point count = {pointCount}");
            Console.WriteLine($"}}");
            Console.WriteLine();
        }

        static void PrintImuValue(AccelFrame frame, string unit)
        {
            var value = frame.GetAccelValue();
            Console.WriteLine($"frame index: {frame.GetIndex()}");
            Console.WriteLine($"OB_FRAME_ACCEL Frame: ");
            Console.WriteLine($"{{");
            Console.WriteLine($"  tsp = {frame.GetTimeStampUs()}");
            Console.WriteLine($"  temperature = {frame.GetTemperature()}");
            Console.WriteLine($"  OB_FRAME_ACCEL.x = {value.x}{unit}");
            Console.WriteLine($"  OB_FRAME_ACCEL.y = {value.y}{unit}");
            Console.WriteLine($"  OB_FRAME_ACCEL.z = {value.z}{unit}");
            Console.WriteLine($"}}");
            Console.WriteLine();
        }

        static void PrintImuValue(GyroFrame frame, string unit)
        {
            var value = frame.GetGyroValue();
            Console.WriteLine($"frame index: {frame.GetIndex()}");
            Console.WriteLine($"OB_FRAME_GYRO Frame: ");
            Console.WriteLine($"{{");
            Console.WriteLine($"  tsp = {frame.GetTimeStampUs()}");
            Console.WriteLine($"  temperature = {frame.GetTemperature()}");
            Console.WriteLine($"  OB_FRAME_GYRO.x = {value.x}{unit}");
            Console.WriteLine($"  OB_FRAME_GYRO.y = {value.y}{unit}");
            Console.WriteLine($"  OB_FRAME_GYRO.z = {value.z}{unit}");
            Console.WriteLine($"}}");
            Console.WriteLine();
        }

        static Device SelectDevice(DeviceList deviceList)
        {
            Console.WriteLine("Device list:");
            for (uint i = 0; i < deviceList.DeviceCount(); ++i)
            {
                Console.WriteLine($"{i}. name: {deviceList.Name(i)}, vid: 0x{deviceList.Vid(i):X4}, pid: 0x{deviceList.Pid(i):X4}, uid: 0x{deviceList.Uid(i)}, sn: {deviceList.SerialNumber(i)}");
            }
            Console.Write("Select a device: ");
            int idx = GetInputOption();
            while (idx < 0 || idx >= deviceList.DeviceCount())
            {
                Console.WriteLine("Your selection is out of range, please reselect:");
                idx = GetInputOption();
            }
            return deviceList.GetDevice((uint)idx);
        }

        static bool IsLiDARDevice(Device device)
        {
            using var info = device.GetDeviceInfo();
            return info.Name().Contains("LiDAR", StringComparison.OrdinalIgnoreCase);
        }
    }
}