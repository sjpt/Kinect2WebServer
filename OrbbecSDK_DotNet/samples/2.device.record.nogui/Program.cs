using Orbbec;
using System.Diagnostics;

namespace Samples.RecordNoGui
{
    class Program
    {
        private static Dictionary<FrameType, ulong> _frameCountMap = new Dictionary<FrameType, ulong>();
        private static readonly object _lock = new object();
        private static volatile bool _isRunning = true;

        static void Main(string[] args)
        {
            Console.Write("Please enter the output filename (with .bag extension) and press Enter to start recording: ");

            string? filePath = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                filePath = "record.bag";
            }

            try
            {
                // Create a context, for getting devices and sensors
                using var context = new Context();

                // Query device list
                using var deviceList = context.QueryDeviceList();
                if (deviceList.DeviceCount() < 1)
                {
                    Console.WriteLine("No device found! Please connect a supported device and retry this program.");
                    Console.WriteLine("\nPress any key to exit.");
                    Console.ReadKey(true);
                    return;
                }

                // Acquire first available device
                using var device = deviceList.GetDevice(0);
                using var devInfo = device.GetDeviceInfo();
                var pidStr = devInfo.Pid();
                var vid = devInfo.Vid();
                // Parse pid from hex string (format: "0x1234")
                var pid = Convert.ToInt32(pidStr.Replace("0x", ""), 16);

                // Create a pipeline with the specified device
                using var pipe = new Pipeline(device);

                // Activate device clock synchronization
                try
                {
                    device.TimerSyncWithHost();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Timer sync warning: {ex.Message}");
                }

                // Create a config and enable all streams
                using var config = new Config();
                using var sensorList = device.GetSensorList();
                var count = sensorList.SensorCount();
                for (uint i = 0; i < count; i++)
                {
                    var sensorType = sensorList.SensorType(i);

                    // Skip IR sensor for Astra Mini devices
                    if (IsAstraMiniDevice(vid, pid) && sensorType == SensorType.OB_SENSOR_IR)
                    {
                        continue;
                    }

                    config.EnableStream(sensorType);
                }

                // Start pipeline with callback
                pipe.Start(config, OnFrameset);

                // Register Ctrl+C handler for graceful shutdown
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    _isRunning = false;
                };

                // Initialize recording device with output file
                var startTime = GetNowTimesMs();
                uint waitTime = 1000;
                using var recordDevice = new RecordDevice(device, filePath, true);

                // Operation prompt
                Console.WriteLine("Streams and recorder have started!");
                Console.WriteLine("Press ESC, 'q', or 'Q' to stop recording and exit safely.");
                Console.WriteLine("IMPORTANT: Always use ESC/q/Q to stop! Otherwise, the bag file will be corrupted and unplayable.");
                Console.WriteLine();

                do
                {
                    // Wait for key with timeout
                    var key = WaitForKeyPressed(waitTime);
                    if (key == 27 || key == 'q' || key == 'Q')  // ESC_KEY = 27
                    {
                        break;
                    }

                    var currentTime = GetNowTimesMs();
                    if (currentTime > startTime + waitTime)
                    {
                        Dictionary<FrameType, ulong> tempCountMap = new Dictionary<FrameType, ulong>();
                        ulong duration;

                        lock (_lock)
                        {
                            // Get time again
                            currentTime = GetNowTimesMs();
                            duration = currentTime - startTime;

                            if (_frameCountMap.Count > 0)
                            {
                                startTime = currentTime;
                                waitTime = 2000;  // Change to 2s for next time
                                tempCountMap = new Dictionary<FrameType, ulong>(_frameCountMap);
                                foreach (var key_type in _frameCountMap.Keys.ToList())
                                {
                                    _frameCountMap[key_type] = 0;  // Reset count
                                }
                            }
                        }

                        string seperate = "";
                        if (tempCountMap.Count == 0)
                        {
                            Console.WriteLine("Recording... Current FPS: 0");
                        }
                        else
                        {
                            Console.Write("Recording... Current FPS: ");
                            foreach (var item in tempCountMap)
                            {
                                var name = ConvertFrameTypeToString(item.Key);
                                float rate = (float)(item.Value / (duration / 1000.0));

                                Console.Write($"{seperate}{name}={rate:F2}");
                                seperate = ", ";
                            }
                            Console.WriteLine();
                        }
                    }
                } while (_isRunning);

                // Stop pipeline before disposing
                pipe.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("\nPress any key to exit.");
                Console.ReadKey(true);
            }
        }

        static void OnFrameset(Frameset frameset)
        {
            try
            {
                lock (_lock)
                {
                    var count = frameset.GetFrameCount();
                    for (uint i = 0; i < count; i++)
                    {
                        using var frame = frameset.GetFrameByIndex((int)i);
                        if (frame != null)
                        {
                            var type = frame.GetFrameType();
                            if (_frameCountMap.ContainsKey(type))
                                _frameCountMap[type]++;
                            else
                                _frameCountMap[type] = 1;
                        }
                    }
                }
            }
            finally
            {
                frameset.Dispose();
            }
        }

        static ulong GetNowTimesMs()
        {
            return (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        static char WaitForKeyPressed(uint timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    return key.KeyChar;
                }
                Thread.Sleep(10);
            }
            return '\0';
        }

        static string ConvertFrameTypeToString(FrameType type)
        {
            return type switch
            {
                FrameType.OB_FRAME_COLOR => "COLOR",
                FrameType.OB_FRAME_DEPTH => "DEPTH",
                FrameType.OB_FRAME_IR => "IR",
                FrameType.OB_FRAME_IR_LEFT => "IR_LEFT",
                FrameType.OB_FRAME_IR_RIGHT => "IR_RIGHT",
                FrameType.OB_FRAME_COLOR_LEFT => "COLOR_LEFT",
                FrameType.OB_FRAME_COLOR_RIGHT => "COLOR_RIGHT",
                FrameType.OB_FRAME_GYRO => "GYRO",
                FrameType.OB_FRAME_ACCEL => "ACCEL",
                FrameType.OB_FRAME_POINTS => "POINTS",
                FrameType.OB_FRAME_UNKNOWN => "UNKNOWN",
                _ => type.ToString().Replace("OB_FRAME_", "")
            };
        }

        static bool IsAstraMiniDevice(int vid, int pid)
        {
            // OB_DEVICE_VID = 0x2bc5
            return vid == 0x2bc5 && (pid == 0x069d || pid == 0x065b || pid == 0x065e);
        }
    }
}
