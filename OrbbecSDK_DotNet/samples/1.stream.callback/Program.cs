using Orbbec;
using Samples.Common;

namespace Samples.Callback
{
    class Program
    {
        private static Frameset? _frameset = null;
        private static readonly object _lock = new object();
        private static volatile bool _isRunning = true;
        private static Pipeline? _pipeline = null;

        // Store detected auxiliary sensor information: (sensor type, frame type, texture index)
        // Includes IR sensors (mono/stereo) and dual color sensors (COLOR_LEFT/COLOR_RIGHT)
        private static List<(SensorType sensorType, FrameType frameType, int textureIndex)> _auxSensors = new();

        // Confidence stream texture index
        private static int _confidenceTextureIndex = -1;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Callback Sample - Starting...");

            using var renderer = new OrbbecRenderer(title: "Callback Sample");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                renderer.Close();
            };

            try
            {
                _pipeline = new Pipeline();
                using var config = new Config();

                // Get device from pipeline
                var device = _pipeline.GetDevice();
                using var deviceInfo = device.GetDeviceInfo();
                string pidStr = deviceInfo.Pid();
                int vid = deviceInfo.Vid();
                // Parse hex string like "0x1001"
                int pid = Convert.ToInt32(pidStr.Replace("0x", ""), 16);

                // Get sensorList from device
                var sensorList = device.GetSensorList();
                uint sensorCount = sensorList.SensorCount();

                Console.WriteLine($"Device: {deviceInfo.Name()}, PID: 0x{pid:X}, VID: 0x{vid:X}");
                Console.WriteLine($"Sensors found: {sensorCount}");

                // Enable all video sensor streams
                for (uint i = 0; i < sensorCount; i++)
                {
                    var sensorType = sensorList.SensorType(i);
                    Console.WriteLine($"  {i}. {sensorType}");

                    // Skip non-video sensor type
                    if (!IsVideoSensorType(sensorType))
                        continue;

                    // Skip IR sensor for Astra Mini devices
                    if (IsAstraMiniDevice(vid, pid) && sensorType == SensorType.OB_SENSOR_IR)
                    {
                        Console.WriteLine("    -> Skipping IR for Astra Mini");
                        continue;
                    }

                    // Enable the stream for the sensor type
                    config.EnableStream(sensorType);
                    Console.WriteLine("    -> Enabled");
                }

                // Start pipeline with callback
                _pipeline.Start(config, OnFrameset);

                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                // Add video frames for rendering
                var colorTextureIndex = renderer.AddVideoFrame();
                var depthTextureIndex = renderer.AddVideoFrame();

                // Dynamically detect auxiliary sensors (IR and dual color) and add textures
                _auxSensors.Clear();
                for (uint i = 0; i < sensorCount; i++)
                {
                    var sensorType = sensorList.SensorType(i);
                    if (sensorType == SensorType.OB_SENSOR_IR ||
                        sensorType == SensorType.OB_SENSOR_IR_LEFT ||
                        sensorType == SensorType.OB_SENSOR_IR_RIGHT ||
                        sensorType == SensorType.OB_SENSOR_COLOR_LEFT ||
                        sensorType == SensorType.OB_SENSOR_COLOR_RIGHT)
                    {
                        var frameType = sensorType switch
                        {
                            SensorType.OB_SENSOR_IR => FrameType.OB_FRAME_IR,
                            SensorType.OB_SENSOR_IR_LEFT => FrameType.OB_FRAME_IR_LEFT,
                            SensorType.OB_SENSOR_IR_RIGHT => FrameType.OB_FRAME_IR_RIGHT,
                            SensorType.OB_SENSOR_COLOR_LEFT => FrameType.OB_FRAME_COLOR_LEFT,
                            SensorType.OB_SENSOR_COLOR_RIGHT => FrameType.OB_FRAME_COLOR_RIGHT,
                            _ => FrameType.OB_FRAME_IR
                        };
                        var textureIndex = renderer.AddVideoFrame();
                        _auxSensors.Add((sensorType, frameType, textureIndex));
                        Console.WriteLine($"    -> Sensor detected: {sensorType}, texture index: {textureIndex}");
                    }
                    else if (sensorType == SensorType.OB_SENSOR_CONFIDENCE)
                    {
                        // Add confidence stream texture
                        _confidenceTextureIndex = renderer.AddVideoFrame();
                        Console.WriteLine($"    -> Confidence sensor detected, texture index: {_confidenceTextureIndex}");
                    }
                }

                // If no auxiliary sensor detected, add a default IR one (for backward compatibility)
                if (_auxSensors.Count == 0)
                {
                    var textureIndex = renderer.AddVideoFrame();
                    _auxSensors.Add((SensorType.OB_SENSOR_IR, FrameType.OB_FRAME_IR, textureIndex));
                    Console.WriteLine($"    -> No IR sensor detected, using default");
                }

                _ = Task.Run(() => RenderFrames(renderer, colorTextureIndex, depthTextureIndex));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                _pipeline?.Stop();
                Console.WriteLine("Callback sample exited.");
            }
        }

        static void OnFrameset(Frameset frameset)
        {
            lock (_lock)
            {
                _frameset?.Dispose();
                _frameset = frameset;
            }
        }

        static void RenderFrames(OrbbecRenderer renderer, int colorIndex, int depthIndex)
        {
            while (_isRunning)
            {
                Frameset? frameset = null;
                lock (_lock)
                {
                    if (_frameset != null)
                    {
                        frameset = _frameset;
                        _frameset = null;
                    }
                }

                if (frameset != null)
                {
                    using (frameset)
                    {
                        var colorFrame = frameset.GetColorFrame();
                        if (colorFrame != null)
                        {
                            using (colorFrame)
                            {
                                using var vf = colorFrame.As<VideoFrame>();
                                if (vf != null)
                                {
                                    byte[] data = new byte[vf.GetDataSize()];
                                    vf.CopyData(ref data);
                                    // Pass the original frame to support formats requiring Filter conversion like MJPG
                                    renderer.UpdateVideoFrame(colorIndex, (int)vf.GetWidth(), (int)vf.GetHeight(),
                                        vf.GetFormat(), data, vf);
                                }
                            }
                        }

                        var depthFrame = frameset.GetDepthFrame();
                        if (depthFrame != null)
                        {
                            using (depthFrame)
                            {
                                byte[] data = new byte[depthFrame.GetDataSize()];
                                depthFrame.CopyData(ref data);
                                renderer.UpdateVideoFrame(depthIndex, (int)depthFrame.GetWidth(),
                                    (int)depthFrame.GetHeight(), depthFrame.GetFormat(), data);
                            }
                        }

                        // Process all auxiliary frames (IR, dual color sensors)
                        foreach (var auxSensor in _auxSensors)
                        {
                            using var auxFrame = frameset.GetFrame(auxSensor.frameType);
                            if (auxFrame != null)
                            {
                                using var vf = auxFrame.As<VideoFrame>();
                                if (vf != null)
                                {
                                    byte[] data = new byte[vf.GetDataSize()];
                                    vf.CopyData(ref data);
                                    // Pass the original frame to support formats requiring Filter conversion
                                    renderer.UpdateVideoFrame(auxSensor.textureIndex, (int)vf.GetWidth(),
                                        (int)vf.GetHeight(), vf.GetFormat(), data, vf);
                                }
                            }
                        }

                        // Process confidence stream
                        if (_confidenceTextureIndex >= 0)
                        {
                            using var confidenceFrame = frameset.GetFrame(FrameType.OB_FRAME_CONFIDENCE);
                            if (confidenceFrame != null)
                            {
                                using var confidenceVideoFrame = confidenceFrame.As<VideoFrame>();
                                if (confidenceVideoFrame != null)
                                {
                                    byte[] data = new byte[confidenceVideoFrame.GetDataSize()];
                                    confidenceVideoFrame.CopyData(ref data);
                                    renderer.UpdateVideoFrame(_confidenceTextureIndex, (int)confidenceVideoFrame.GetWidth(),
                                        (int)confidenceVideoFrame.GetHeight(), confidenceVideoFrame.GetFormat(), data);
                                }
                            }
                        }
                    }
                }

                Thread.Sleep(10);
            }
        }

        static bool IsVideoSensorType(SensorType type)
        {
            return type == SensorType.OB_SENSOR_COLOR ||
                   type == SensorType.OB_SENSOR_DEPTH ||
                   type == SensorType.OB_SENSOR_IR ||
                   type == SensorType.OB_SENSOR_IR_LEFT ||
                   type == SensorType.OB_SENSOR_IR_RIGHT ||
                   type == SensorType.OB_SENSOR_COLOR_LEFT ||
                   type == SensorType.OB_SENSOR_COLOR_RIGHT ||
                   type == SensorType.OB_SENSOR_CONFIDENCE;
        }

        static bool IsAstraMiniDevice(int vid, int pid)
        {
            // OB_DEVICE_VID = 0x2bc5
            return vid == 0x2bc5 && (pid == 0x069d || pid == 0x065b || pid == 0x065e);
        }
    }
}