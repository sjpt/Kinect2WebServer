using Orbbec;
using Samples.Common;
using System.Runtime.InteropServices;

namespace Samples.Decimation
{
    class Program
    {
        private static volatile bool _isRunning = true;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Decimation Stream Sample - Starting...");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _isRunning = false;
            };

            Pipeline? pipeline = null;
            Config? config = null;
            OrbbecRenderer? renderer = null;
            try
            {
                pipeline = new Pipeline();

                // Get the device with the pipeline
                var device = pipeline.GetDevice();
                if (device == null)
                {
                    Console.WriteLine("No device found!");
                    Console.WriteLine("\nPress any key to exit.");
                    Console.ReadKey(true);
                    return;
                }

                // Check if device supports preset resolution configuration first
                bool isPropertySupported = device.IsPropertySupported(PropertyId.OB_STRUCT_PRESET_RESOLUTION_CONFIG, PermissionType.OB_PERMISSION_READ_WRITE);
                if (!isPropertySupported)
                {
                    Console.WriteLine("The device does not support preset resolution configuration.");
                    Console.WriteLine("\nPress any key to exit.");
                    Console.ReadKey(true);
                    return;
                }

                // Retrieve device info VID/PID
                using var deviceInfo = device.GetDeviceInfo();
                var vid = deviceInfo.Vid();
                var pidStr = deviceInfo.Pid();
                int pid = int.Parse(pidStr.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);

                Console.WriteLine($"Device connected: {deviceInfo.Name()} (VID: 0x{vid:X4}, PID: 0x{pid:X4})");

                // Specific check for Gemini 305 device
                if (!IsGemini305Device(vid, pid))
                {
                    EnumeratePresetResolutionConfig(device);
                }

                // Configure Sensors and Streams
                config = new Config();
                Console.WriteLine("Configuring Sensors...");

                using var sensorList = device.GetSensorList();
                for (uint index = 0; index < sensorList.SensorCount(); index++)
                {
                    SensorType sensorType = sensorList.SensorType(index);

                    if (IsIRSensor(sensorType) || sensorType == SensorType.OB_SENSOR_DEPTH)
                    {
                        using var sensor = sensorList.GetSensor(index);
                        Console.WriteLine($"\n[Sensor {index}]: {sensorType}");

                        // User selects a profile (Resolution/FPS)
                        using var profile = SelectStreamProfile(sensor, sensorType);

                        if (profile != null)
                        {
                            // Enable this specific stream profile in the pipeline configuration
                            config.EnableStream(profile);
                            Console.WriteLine(" -> Stream enabled.");
                        }
                    }
                }

                // All user input is complete, now create window before starting stream
                Console.WriteLine("\nInitializing window...");
                renderer = new OrbbecRenderer(title: "Infrared/Depth Viewer");
                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                // Start the Pipeline
                Console.WriteLine("\nStarting Pipeline...");
                pipeline.Start(config);

                // Create textures for rendering - create textures for Depth, Left IR, and Right IR separately
                int depthTextureIndex = renderer.AddVideoFrame();
                int irLeftTextureIndex = renderer.AddVideoFrame();
                int irRightTextureIndex = renderer.AddVideoFrame();

                _ = Task.Run(() => StartStream(pipeline, renderer, depthTextureIndex, irLeftTextureIndex, irRightTextureIndex));

                Console.WriteLine("Pipeline started. Rendering... (Press ESC to close)");
                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("\nPress any key to exit.");
                Console.ReadKey(true);
            }
            finally
            {
                pipeline?.Stop();
                config?.Dispose();
                renderer?.Close();
                renderer?.Dispose();
                Console.WriteLine("Decimation sample exited.");
            }
        }

        // Gets user input for index selection
        static uint GetUserInput(uint maxIndex)
        {
            int selected = -1;
            while (true)
            {
                Console.Write($"Please input the index (0 - {maxIndex - 1}): ");
                var input = Console.ReadLine();

                if (!int.TryParse(input, out selected))
                {
                    Console.WriteLine("Invalid input, please enter a number.");
                    continue;
                }

                if (selected < 0 || selected >= maxIndex)
                {
                    Console.WriteLine("Index out of range. Please try again.");
                    continue;
                }

                break;
            }
            return (uint)selected;
        }

        // Prints the details of a stream profile (Resolution, FPS, Format)
        static void PrintStreamProfile(StreamProfile profile, uint index)
        {
            using var videoProfile = profile.As<VideoStreamProfile>();
            if (videoProfile == null)
                return;

            var formatName = profile.GetFormat();
            var width = videoProfile.GetWidth();
            var height = videoProfile.GetHeight();
            var fps = videoProfile.GetFPS();
            var decimationConfig = videoProfile.GetDecimationConfig();

            Console.Write($"{index}. format: {formatName}, res: {width}*{height}, fps: {fps}");

            // If decimation is active, print origin details
            if (decimationConfig.factor != 0)
            {
                Console.Write($", originRes: {decimationConfig.originWidth}*{decimationConfig.originHeight}, decimation factor: {decimationConfig.factor}");
            }
            Console.WriteLine();
        }

        // Enumerates and sets preset resolution configurations
        static void EnumeratePresetResolutionConfig(Device device)
        {
            Console.WriteLine("[Notice]: This device requires a Preset Resolution Configuration.");
            Console.WriteLine("Preset resolution config list: ");

            using var presetList = device.GetAvailablePresetResolutionConfigList();
            var presetNum = presetList.Count();

            if (presetNum == 0)
            {
                Console.WriteLine("No preset resolution config available");
                return;
            }

            // List all available presets
            for (uint index = 0; index < presetNum; index++)
            {
                var presetConfig = presetList.GetPresetResolutionRatioConfig(index);
                Console.WriteLine($"{index}. width {presetConfig.width}, height {presetConfig.height}, IR decimation {presetConfig.irDecimationFactor}, Depth decimation {presetConfig.depthDecimationFactor}");
            }

            // Get User Selection
            uint selected = GetUserInput(presetNum);

            // Apply the configuration to the device
            var selectedPresetConfig = presetList.GetPresetResolutionRatioConfig(selected);
            device.SetStructuredData(PropertyId.OB_STRUCT_PRESET_RESOLUTION_CONFIG, selectedPresetConfig);
            Console.WriteLine("Preset configuration applied.");
        }

        // Displays available stream profiles for a sensor and lets the user select one
        static StreamProfile? SelectStreamProfile(Sensor sensor, SensorType sensorType)
        {
            using var streamList = sensor.GetStreamProfileList();
            if (streamList.ProfileCount() == 0)
            {
                Console.WriteLine("No stream profiles available for this sensor.");
                return null;
            }

            Console.WriteLine($"Available profiles for {sensorType}:");
            for (int i = 0; i < streamList.ProfileCount(); ++i)
            {
                using var profile = streamList.GetProfile(i);
                PrintStreamProfile(profile, (uint)i);
            }

            // Special hint for IR sensors regarding synchronization
            if (IsIRSensor(sensorType))
            {
                Console.WriteLine("[Note]: Please keep the original resolution and decimation factor consistent with Depth sensor.");
            }

            uint selected = GetUserInput((uint)streamList.ProfileCount());

            return streamList.GetProfile((int)selected);
        }

        // Check if sensor is an IR sensor
        static bool IsIRSensor(SensorType sensorType)
        {
            return sensorType == SensorType.OB_SENSOR_IR ||
                   sensorType == SensorType.OB_SENSOR_IR_LEFT ||
                   sensorType == SensorType.OB_SENSOR_IR_RIGHT;
        }

        // Check if device is Gemini 305
        static bool IsGemini305Device(int vid, int pid)
        {
            // Gemini 305 devices have VID 0x2BC5 and PID 0x0840, 0x0841, 0x0842, or 0x0843
            return vid == 0x2BC5 && (pid == 0x0840 || pid == 0x0841 || pid == 0x0842 || pid == 0x0843);
        }

        static void StartStream(Pipeline pipeline, OrbbecRenderer renderer, int depthTextureIndex, int irLeftTextureIndex, int irRightTextureIndex)
        {
            try
            {
                while (_isRunning)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    // Process Depth Frame
                    using var depthFrame = frameSet.GetDepthFrame();
                    if (depthFrame != null)
                    {
                        try
                        {
                            byte[] data = new byte[depthFrame.GetDataSize()];
                            depthFrame.CopyData(ref data);
                            renderer.UpdateVideoFrame(depthTextureIndex, (int)depthFrame.GetWidth(),
                                (int)depthFrame.GetHeight(), depthFrame.GetFormat(), data, depthFrame);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Depth] Frame processing error: {ex.Message}");
                        }
                    }

                    // Process IR Left Frame
                    ProcessIRFrame(frameSet, renderer, FrameType.OB_FRAME_IR_LEFT, irLeftTextureIndex, "IR_LEFT");

                    // Process IR Right Frame
                    ProcessIRFrame(frameSet, renderer, FrameType.OB_FRAME_IR_RIGHT, irRightTextureIndex, "IR_RIGHT");

                    // Process generic IR Frame (if no left/right)
                    using var irFrame = frameSet.GetFrame(FrameType.OB_FRAME_IR);
                    if (irFrame != null)
                    {
                        try
                        {
                            using var irVideoFrame = irFrame.As<VideoFrame>();
                            if (irVideoFrame != null)
                            {
                                byte[] data = new byte[irVideoFrame.GetDataSize()];
                                irVideoFrame.CopyData(ref data);
                                renderer.UpdateVideoFrame(irLeftTextureIndex, (int)irVideoFrame.GetWidth(),
                                    (int)irVideoFrame.GetHeight(), irVideoFrame.GetFormat(), data, irVideoFrame);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[IR] Frame processing error: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Stream error: {ex.Message}");
            }
        }

        static void ProcessIRFrame(Frameset frameSet, OrbbecRenderer renderer, FrameType frameType, int textureIndex, string frameName)
        {
            try
            {
                using var irFrame = frameSet.GetFrame(frameType);
                if (irFrame != null)
                {
                    using var irVideoFrame = irFrame.As<VideoFrame>();
                    if (irVideoFrame != null)
                    {
                        byte[] data = new byte[irVideoFrame.GetDataSize()];
                        irVideoFrame.CopyData(ref data);
                        renderer.UpdateVideoFrame(textureIndex, (int)irVideoFrame.GetWidth(),
                            (int)irVideoFrame.GetHeight(), irVideoFrame.GetFormat(), data, irVideoFrame);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{frameName}] Frame processing error: {ex.Message}");
            }
        }
    }
}
