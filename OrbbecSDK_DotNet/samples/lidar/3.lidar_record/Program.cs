using Orbbec;
using Samples.Common;
using System.Collections.Concurrent;

namespace LiDAR.Record
{
    class Program
    {
        static ConcurrentDictionary<FrameType, ulong> frameCountMap = new();
        static volatile bool isRunning = true;
        static DateTime startTime = DateTime.Now;
        static int waitTime = 1000;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("LiDAR Record - Starting...");

            try
            {
                using var context = new Context();
                using var deviceList = context.QueryDeviceList();

                if (deviceList.DeviceCount() < 1)
                {
                    Console.WriteLine("No device found! Please connect a LiDAR device and retry.");
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

                // Get output filename
                Console.Write("\nPlease enter the output filename (with .bag extension): ");
                string? filePath = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(filePath))
                {
                    filePath = "lidar_record.bag";
                }
                if (!filePath.EndsWith(".bag"))
                {
                    filePath += ".bag";
                }

                using var pipe = new Pipeline(device);
                using var config = new Config();
                using var recordDevice = new RecordDevice(device, filePath);

                // Enable all streams
                using var sensorList = device.GetSensorList();
                for (uint i = 0; i < sensorList.SensorCount(); ++i)
                {
                    var sensorType = sensorList.SensorType(i);
                    config.EnableStream(sensorType);
                    Console.WriteLine($"Enabled stream for: {sensorType}");
                }

                // Start pipeline with frame callback
                pipe.Start(config, OnFrameSet);

                Console.WriteLine("\nStreams and recorder have started!");
                Console.WriteLine("Press ESC, 'q', or 'Q' to stop recording.");
                Console.WriteLine("IMPORTANT: Always use ESC/q/Q to stop! Otherwise, the bag file will be corrupted.");

                // Start FPS display thread
                var fpsThread = new Thread(DisplayFPS);
                fpsThread.Start();

                // Wait for exit
                while (isRunning)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Escape || key == ConsoleKey.Q)
                        {
                            isRunning = false;
                            break;
                        }
                    }
                    Thread.Sleep(10);
                }

                // Stop pipeline
                pipe.Stop();

                Console.WriteLine($"\nRecording saved to: {filePath}");
                Console.WriteLine("LiDAR Record exited.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static void OnFrameSet(Frameset frameset)
        {
            if (frameset == null) return;

            try
            {
                // Get frames by type
                var frameTypes = new[] { FrameType.OB_FRAME_LIDAR_POINTS, FrameType.OB_FRAME_ACCEL, FrameType.OB_FRAME_GYRO, FrameType.OB_FRAME_COLOR, FrameType.OB_FRAME_DEPTH };

                foreach (var type in frameTypes)
                {
                    using var frame = frameset.GetFrame(type);
                    if (frame != null)
                    {
                        frameCountMap[type] = frameCountMap.GetOrAdd(type, 0) + 1;
                    }
                }
            }
            finally
            {
                frameset.Dispose();
            }
        }

        static void DisplayFPS()
        {
            while (isRunning)
            {
                Thread.Sleep(waitTime);

                var currentTime = DateTime.Now;
                var duration = (currentTime - startTime).TotalSeconds;

                if (duration < 0.5) continue;

                var tempCountMap = new ConcurrentDictionary<FrameType, ulong>(frameCountMap);

                if (tempCountMap.IsEmpty)
                {
                    Console.WriteLine("Recording... Current FPS: 0");
                }
                else
                {
                    Console.Write("Recording... Current FPS: ");
                    bool first = true;
                    foreach (var item in tempCountMap)
                    {
                        float rate = (float)(item.Value / duration);
                        if (!first) Console.Write(", ");
                        Console.Write($"{item.Key}={rate:F2}");
                        first = false;
                    }
                    Console.WriteLine();
                }

                // Reset for next interval
                startTime = currentTime;
                waitTime = 2000;
                frameCountMap.Clear();
            }
        }

        static Device SelectDevice(DeviceList deviceList)
        {
            Console.WriteLine("Device list:");
            for (uint i = 0; i < deviceList.DeviceCount(); ++i)
            {
                Console.WriteLine($"{i}. {deviceList.Name(i)}");
            }
            Console.Write("Select: ");
            int idx = int.Parse(Console.ReadLine() ?? "0");
            return deviceList.GetDevice((uint)idx);
        }

        static bool IsLiDARDevice(Device device)
        {
            using var info = device.GetDeviceInfo();
            return info.Name().Contains("LiDAR", StringComparison.OrdinalIgnoreCase);
        }
    }
}