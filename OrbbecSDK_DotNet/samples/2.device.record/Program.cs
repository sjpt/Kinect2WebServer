using Orbbec;
using Samples.Common;

namespace Samples.Record
{
    class Program
    {
        private static volatile bool _isRunning = true;
        private static volatile bool _isPaused = false;
        private static readonly Dictionary<FrameType, ulong> _frameCountMap = new();
        private static readonly object _lock = new();

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Record - Starting...");

            Context? ctx = null;
            Pipeline? pipe = null;
            Device? device = null;
            try
            {
                string? filePath;
                do
                {
                    Console.Write("Please enter the output filename (with .bag extension) and press Enter to start recording: ");
                    filePath = Console.ReadLine()?.Trim();
                } while (string.IsNullOrEmpty(filePath));

                using var renderer = new OrbbecRenderer(title: "Record");

                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    renderer.Close();
                };

                ctx = new Context();

                using var deviceList = ctx.QueryDeviceList();
                if (deviceList.DeviceCount() < 1)
                {
                    Console.WriteLine("No device found! Please connect a supported device and retry this program.");
                    return;
                }

                device = deviceList.GetDevice(0);
                using var devInfo = device.GetDeviceInfo();
                var pidStr = devInfo.Pid();
                var vid = devInfo.Vid();
                // Parse pid from hex string (format: "0x1234")
                var pid = Convert.ToInt32(pidStr.Replace("0x", ""), 16);

                try
                {
                    device.TimerSyncWithHost();
                }
                catch
                {
                    Console.WriteLine("Failed to synchronize the timer of the device with the host.");
                }

                pipe = new Pipeline(device);
                using var config = new Config();

                var textureIndices = new Dictionary<SensorType, int>();
                using var sensorList = device.GetSensorList();
                for (uint i = 0; i < sensorList.SensorCount(); ++i)
                {
                    var sensorType = sensorList.SensorType(i);
                    if (sensorType == SensorType.OB_SENSOR_ACCEL || sensorType == SensorType.OB_SENSOR_GYRO)
                    {
                        config.EnableStream(sensorType);
                        continue;
                    }

                    // Skip IR sensor for Astra Mini devices
                    if (IsAstraMiniDevice(vid, pid) && sensorType == SensorType.OB_SENSOR_IR)
                    {
                        continue;
                    }

                    config.EnableStream(sensorType);
                    Console.WriteLine($"Enabled stream for: {sensorType}");
                    textureIndices.Add(sensorType, renderer.AddVideoFrame());
                }
                pipe.Start(config);

                // Initialize recording device after pipeline starts to ensure proper frame subscription
                using var recordDevice = new RecordDevice(device, filePath);

                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    if (_isPaused)
                    {
                        recordDevice.Resume();
                        _isPaused = false;
                    }
                    _isRunning = false;
                };

                _ = Task.Run(async () => await HandleKeyPress(recordDevice));
                _ = Task.Run(() => StartStream(pipe, renderer, textureIndices));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                pipe?.Stop();
                pipe?.Dispose();
                device?.Dispose();
                ctx?.Dispose();
                Console.WriteLine("Record sample exited.");
            }
        }

        private static async Task HandleKeyPress(RecordDevice recorder)
        {
            Console.WriteLine("Press 'S' to pause/resume recording.");
            while (_isRunning)
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(500);
                    continue;
                }

                var keyInfo = Console.ReadKey(true);
                if (keyInfo.Key == ConsoleKey.S)
                {
                    if (!_isPaused)
                    {
                        recorder.Pause();
                        _isPaused = true;
                        Console.WriteLine("[PAUSED] Recording paused");
                    }
                    else
                    {
                        recorder.Resume();
                        _isPaused = false;
                        Console.WriteLine("[RESUMED] Recording resumed");
                    }
                }
            }
        }

        private static void StartStream(Pipeline pipeline, OrbbecRenderer renderer, Dictionary<SensorType, int> textureIndices)
        {
            try
            {
                while (_isRunning)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    using var accelFrameRaw = frameSet.GetFrame(FrameType.OB_FRAME_ACCEL);
                    if (accelFrameRaw != null)
                    {
                        using var accelFrame = accelFrameRaw.As<AccelFrame>();
                        var accelIndex = accelFrame.GetIndex();
                        var accelTimeStampUs = accelFrame.GetTimeStampUs();
                        var accelTemperature = accelFrame.GetTemperature();
                        var accelType = accelFrame.GetFrameType();
                        if (accelIndex % 50 == 0)
                        {
                            // print information every  50 frames.
                            var accelValue = accelFrame.GetAccelValue();
                            var obFloat3d = new Float3D { x = accelValue.x, y = accelValue.y, z = accelValue.z };
                            PrintImuValue(obFloat3d, accelIndex, accelTimeStampUs, accelTemperature, accelType, "m/s^2");
                        }
                    }

                    using var gyroFrameRaw = frameSet.GetFrame(FrameType.OB_FRAME_GYRO);
                    if (gyroFrameRaw != null)
                    {
                        using var gyroFrame = gyroFrameRaw.As<GyroFrame>();
                        var gyroIndex = gyroFrame.GetIndex();
                        var gyroTimeStampUs = gyroFrame.GetTimeStampUs();
                        var gyroTemperature = gyroFrame.GetTemperature();
                        var gyroType = gyroFrame.GetFrameType();
                        if (gyroIndex % 50 == 0)
                        {
                            // print information every 50 frames.
                            var gyroValue = gyroFrame.GetGyroValue();
                            var obFloat3d = new Float3D { x = gyroValue.x, y = gyroValue.y, z = gyroValue.z };
                            PrintImuValue(obFloat3d, gyroIndex, gyroTimeStampUs, gyroTemperature, gyroType, "rad/s");
                        }
                    }

                    foreach (var (sensorType, textureIndex) in textureIndices)
                    {
                        var frameType = sensorType switch
                        {
                            SensorType.OB_SENSOR_COLOR => FrameType.OB_FRAME_COLOR,
                            SensorType.OB_SENSOR_DEPTH => FrameType.OB_FRAME_DEPTH,
                            SensorType.OB_SENSOR_IR => FrameType.OB_FRAME_IR,
                            SensorType.OB_SENSOR_IR_LEFT => FrameType.OB_FRAME_IR_LEFT,
                            SensorType.OB_SENSOR_IR_RIGHT => FrameType.OB_FRAME_IR_RIGHT,
                            SensorType.OB_SENSOR_COLOR_LEFT => FrameType.OB_FRAME_COLOR_LEFT,
                            SensorType.OB_SENSOR_COLOR_RIGHT => FrameType.OB_FRAME_COLOR_RIGHT,
                            SensorType.OB_SENSOR_CONFIDENCE => FrameType.OB_FRAME_CONFIDENCE,
                            _ => FrameType.OB_FRAME_UNKNOWN
                        };

                        using var frame = frameSet.GetFrame(frameType);

                        if (frame != null)
                        {
                            // Update frame count
                            lock (_lock)
                            {
                                if (_frameCountMap.ContainsKey(frameType))
                                    _frameCountMap[frameType]++;
                                else
                                    _frameCountMap[frameType] = 1;
                            }

                            if (frameType == FrameType.OB_FRAME_CONFIDENCE)
                            {
                                using var depthFrameRaw = frameSet.GetFrame(FrameType.OB_FRAME_DEPTH);
                                if (depthFrameRaw == null)
                                {
                                    continue;
                                }
                                using var depthFrame = depthFrameRaw.As<VideoFrame>();
                                byte[] data = new byte[frame.GetDataSize()];
                                frame.CopyData(ref data);
                                renderer.UpdateVideoFrame(textureIndex, (int)depthFrame.GetWidth(), (int)depthFrame.GetHeight(), Format.OB_FORMAT_Y8, data);
                            }
                            else
                            {
                                using var vf = frame.As<VideoFrame>();
                                byte[] data = new byte[vf.GetDataSize()];
                                vf.CopyData(ref data);
                                // Pass the original frame to support formats requiring Filter conversion like MJPG
                                renderer.UpdateVideoFrame(textureIndex, (int)vf.GetWidth(), (int)vf.GetHeight(), vf.GetFormat(), data, vf);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Start stream error: {ex.Message}");
            }
        }

        private static void PrintImuValue(Float3D obFloat3d, ulong index, ulong timeStampUs, float temperature, FrameType type, string unit)
        {
            Console.WriteLine("frame index: " + index);
            string typeStr = type.ToString();
            Console.WriteLine($"{typeStr} Frame: ");
            Console.WriteLine("{");
            Console.WriteLine($"  tsp = {timeStampUs}");
            Console.WriteLine($"  temperature = {temperature}");
            Console.WriteLine($"  {typeStr}.x = {obFloat3d.x}{unit}");
            Console.WriteLine($"  {typeStr}.y = {obFloat3d.y}{unit}");
            Console.WriteLine($"  {typeStr}.z = {obFloat3d.z}{unit}");
            Console.WriteLine("}");
            Console.WriteLine();
        }

        static bool IsAstraMiniDevice(int vid, int pid)
        {
            // OB_DEVICE_VID = 0x2bc5
            return vid == 0x2bc5 && (pid == 0x069d || pid == 0x065b || pid == 0x065e);
        }
    }
}