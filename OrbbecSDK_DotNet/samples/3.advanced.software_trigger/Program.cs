// Copyright (c) Orbbec Inc. All Rights Reserved.
// Licensed under the MIT License.

using Orbbec;
using Samples.Common;
using System.Threading;

namespace Samples.SoftwareTrigger
{
    class Program
    {
        // Constants for timestamp sync check
        private const long MAX_TIMESTAMP_DIFF_US = 100000; // 100ms in microseconds
        private const int MAX_RETRY_COUNT = 5;

        private static volatile bool _isRunning = true;
        private static Pipeline? _pipeline;
        private static Device? _device;
        private static OrbbecRenderer? _renderer;
        private static int _depthTextureIndex = -1;
        private static int _colorTextureIndex = -1;
        private static ulong _frameCount = 0;
        private static FormatConvertFilter? _mjpegConverter = null;
        private static bool _exitHandled = false;
        private static bool _keepSoftwareTriggerMode = false;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Software Trigger Sample - Starting...");
            Console.WriteLine("This sample demonstrates single device software trigger mode.");
            Console.WriteLine("Press SPACE to trigger capture, ESC to quit.");
            Console.WriteLine("On exit, you can choose to restore default mode (Standalone) or keep software trigger mode.");

            try
            {
                // Create context and query device
                using var context = new Context();
                using var devList = context.QueryDeviceList();
                uint devCount = devList.DeviceCount();

                if (devCount == 0)
                {
                    Console.WriteLine("No device found! Please connect a device.");
                    Console.ReadKey();
                    return;
                }

                Console.WriteLine($"Found {devCount} device(s)");

                // Get the first device
                _device = devList.GetDevice(0);
                using var deviceInfo = _device.GetDeviceInfo();
                Console.WriteLine($"Device: {deviceInfo.Name()}");
                Console.WriteLine($"SN: {deviceInfo.SerialNumber()}");

                // Check if device supports software trigger mode
                var supportedSyncModeBitmap = _device.GetSupportedMultiDeviceSyncModeBitmap();
                bool supportsSoftwareTrigger = (supportedSyncModeBitmap & (1 << 5)) != 0; // OB_MULTI_DEVICE_SYNC_MODE_SOFTWARE_TRIGGERING = 1 << 5

                if (!supportsSoftwareTrigger)
                {
                    Console.WriteLine("\nDevice does not support software trigger mode!");
                    Console.WriteLine($"Supported sync mode bitmap: 0x{supportedSyncModeBitmap:X4}");
                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey();
                    return;
                }

                Console.WriteLine("Device supports software trigger mode.");

                // Verify sensor availability
                var sensorList = _device.GetSensorList();
                Console.WriteLine($"Available sensors: {sensorList.SensorCount()}");
                for (uint i = 0; i < sensorList.SensorCount(); i++)
                {
                    Console.WriteLine($"  Sensor {i}: {sensorList.SensorType(i)}");
                }

                // Set sync mode to software trigger BEFORE creating pipeline
                var syncConfig = _device.GetMultiDeviceSyncConfig();
                Console.WriteLine($"Current sync mode: {syncConfig.syncMode}");

                syncConfig.syncMode = MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_SOFTWARE_TRIGGERING;
                syncConfig.framesPerTrigger = 1;
                _device.SetMultiDeviceSyncConfig(syncConfig);
                Console.WriteLine("Sync mode set to: OB_MULTI_DEVICE_SYNC_MODE_SOFTWARE_TRIGGERING");

                // Verify the sync mode was set
                var verifyConfig = _device.GetMultiDeviceSyncConfig();
                Console.WriteLine($"Verified sync mode: {verifyConfig.syncMode}");

                // Create pipeline AFTER setting sync mode
                _pipeline = new Pipeline(_device);

                // Enable color and depth streams
                using var config = new Config();
                config.EnableStream(SensorType.OB_SENSOR_COLOR);
                config.EnableStream(SensorType.OB_SENSOR_DEPTH);

                // Start pipeline WITHOUT callback
                _pipeline.Start(config);
                Console.WriteLine("Pipeline started");

                // Create MJPEG converter for color stream
                _mjpegConverter = new FormatConvertFilter();
                _mjpegConverter.SetConvertFormat(ConvertFormat.FORMAT_MJPG_TO_RGB);

                // Create renderer
                _renderer = new OrbbecRenderer(
                    width: 1280,
                    height: 720,
                    title: "Software Trigger Viewer");

                // Add video frames
                _colorTextureIndex = _renderer.AddVideoFrame();
                _depthTextureIndex = _renderer.AddVideoFrame();

                Console.WriteLine($"Color texture index: {_colorTextureIndex}");
                Console.WriteLine($"Depth texture index: {_depthTextureIndex}");

                // Setup window closing event
                _renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                    HandleExit();
                };

                Console.WriteLine("\nReady! Press SPACE to capture, ESC to quit.\n");

                // Start capture loop on background thread
                Task.Run(() => CaptureLoop());

                // Run renderer on main thread
                _renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                Cleanup();
            }
        }

        private static void CaptureLoop()
        {
            try
            {
                while (_isRunning)
                {
                    // Check for key press without blocking
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Spacebar)
                        {
                            TriggerAndProcessFrames();
                        }
                        else if (key.Key == ConsoleKey.Escape)
                        {
                            Console.WriteLine("ESC pressed, quitting...");
                            _isRunning = false;
                            HandleExit();
                            _renderer?.Close();
                            break;
                        }
                    }
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Capture loop error: {ex.Message}");
            }
        }

        private static void TriggerAndProcessFrames()
        {
            if (_device == null || _pipeline == null || _renderer == null)
            {
                Console.WriteLine("ERROR: Device, Pipeline or Renderer is null!");
                return;
            }

            int triggerAttempt = 0;
            bool captureSuccess = false;

            while (triggerAttempt < MAX_RETRY_COUNT && !captureSuccess)
            {
                triggerAttempt++;
                if (triggerAttempt > 1)
                {
                    Console.WriteLine($"Retry trigger attempt {triggerAttempt}/{MAX_RETRY_COUNT}...");
                }

                try
                {
                    Console.WriteLine("Triggering capture...");
                    _device.TriggerCapture();
                    Console.WriteLine("Trigger sent");

                    bool colorOK = false;
                    bool depthOK = false;
                    int retryCount = 0;
                    const int maxRetries = 100;

                    byte[]? colorData = null;
                    byte[]? depthData = null;
                    int colorWidth = 0, colorHeight = 0;
                    int depthWidth = 0, depthHeight = 0;
                    Format colorFormat = Format.OB_FORMAT_UNKNOWN;
                    Format depthFormat = Format.OB_FORMAT_UNKNOWN;
                    long colorTimestamp = 0;
                    long depthTimestamp = 0;

                    while (_isRunning && (!colorOK || !depthOK) && retryCount < maxRetries)
                    {
                        using var frames = _pipeline.WaitForFrames(100);
                        if (frames == null)
                        {
                            retryCount++;
                            continue;
                        }

                        // Process color frame
                        if (!colorOK)
                        {
                            using var colorFrame = frames.GetFrame(FrameType.OB_FRAME_COLOR);
                            if (colorFrame != null)
                            {
                                colorTimestamp = (long)colorFrame.GetTimeStampUs();
                                Console.WriteLine($"[Frame #{++_frameCount}] Color captured - Ts: {colorTimestamp}us");

                                using var videoFrame = colorFrame.As<VideoFrame>();
                                if (videoFrame != null)
                                {
                                    colorWidth = (int)videoFrame.GetWidth();
                                    colorHeight = (int)videoFrame.GetHeight();
                                }
                                colorFormat = colorFrame.GetFormat();

                                if (colorFormat == Format.OB_FORMAT_MJPG && _mjpegConverter != null)
                                {
                                    using var convertedFrame = _mjpegConverter.Process(colorFrame);
                                    if (convertedFrame != null)
                                    {
                                        using var streamProfile = convertedFrame.GetStreamProfile();
                                        using var videoProfile = streamProfile.As<VideoStreamProfile>();
                                        colorWidth = (int)videoProfile.GetWidth();
                                        colorHeight = (int)videoProfile.GetHeight();
                                        colorFormat = Format.OB_FORMAT_RGB;

                                        colorData = new byte[convertedFrame.GetDataSize()];
                                        convertedFrame.CopyData(ref colorData);
                                    }
                                }
                                else
                                {
                                    colorData = new byte[colorFrame.GetDataSize()];
                                    colorFrame.CopyData(ref colorData);
                                }
                                colorOK = true;
                            }
                        }

                        // Process depth frame
                        if (!depthOK)
                        {
                            using var depthFrame = frames.GetFrame(FrameType.OB_FRAME_DEPTH);
                            if (depthFrame != null)
                            {
                                depthTimestamp = (long)depthFrame.GetTimeStampUs();
                                Console.WriteLine($"[Frame #{_frameCount}] Depth captured - Ts: {depthTimestamp}us");

                                using var videoFrame = depthFrame.As<VideoFrame>();
                                if (videoFrame != null)
                                {
                                    depthWidth = (int)videoFrame.GetWidth();
                                    depthHeight = (int)videoFrame.GetHeight();
                                }
                                depthFormat = depthFrame.GetFormat();

                                depthData = new byte[depthFrame.GetDataSize()];
                                depthFrame.CopyData(ref depthData);
                                depthOK = true;
                            }
                        }

                        if (!colorOK || !depthOK)
                        {
                            retryCount++;
                        }
                    }

                    // Check if frames were captured successfully
                    if (!colorOK || !depthOK)
                    {
                        if (retryCount >= maxRetries)
                        {
                            if (_frameCount == 0)
                            {
                                Console.WriteLine("First trigger timeout (device warmup). Try again.");
                            }
                            else
                            {
                                Console.WriteLine("WARNING: Timeout waiting for frames!");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Partial capture: Color={colorOK}, Depth={depthOK}");
                        }
                        continue; // Retry trigger
                    }

                    // Check timestamp sync
                    long timestampDiff = Math.Abs(colorTimestamp - depthTimestamp);
                    Console.WriteLine($"Timestamp diff: {timestampDiff}us ({timestampDiff / 1000.0:F2}ms)");

                    if (timestampDiff > MAX_TIMESTAMP_DIFF_US)
                    {
                        Console.WriteLine($"WARNING: Timestamp diff ({timestampDiff / 1000.0:F2}ms) > threshold ({MAX_TIMESTAMP_DIFF_US / 1000}ms), will retry...");
                        continue; // Retry trigger
                    }

                    // Update renderer with captured data
                    if (colorData != null)
                    {
                        _renderer.UpdateVideoFrame(_colorTextureIndex, colorWidth, colorHeight, colorFormat, colorData);
                    }

                    if (depthData != null)
                    {
                        _renderer.UpdateVideoFrame(_depthTextureIndex, depthWidth, depthHeight, depthFormat, depthData);
                    }

                    Console.WriteLine($"Capture successful! Color:{colorWidth}x{colorHeight}, Depth:{depthWidth}x{depthHeight}, Sync diff:{timestampDiff / 1000.0:F2}ms");
                    captureSuccess = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Trigger error: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }

            if (!captureSuccess)
            {
                Console.WriteLine($"ERROR: Failed to capture synchronized frames after {MAX_RETRY_COUNT} attempts!");
            }
        }

        private static void Cleanup()
        {
            _isRunning = false;

            // Stop pipeline first
            try { _pipeline?.Stop(); } catch { }

            // Dispose renderer and converter to release device resources
            _renderer?.Dispose();
            _mjpegConverter?.Dispose();

            // Restore default sync mode after releasing device resources but before disposing device
            if (!_keepSoftwareTriggerMode && _device != null)
            {
                try
                {
                    Console.WriteLine("Restoring default sync mode (Standalone)...");
                    var syncConfig = _device.GetMultiDeviceSyncConfig();
                    syncConfig.syncMode = MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_STANDALONE;
                    _device.SetMultiDeviceSyncConfig(syncConfig);

                    var verifyConfig = _device.GetMultiDeviceSyncConfig();
                    Console.WriteLine($"Sync mode restored to: {verifyConfig.syncMode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to restore sync mode: {ex.Message}");
                }
            }

            try { _pipeline?.Dispose(); } catch { }
            _device?.Dispose();

            Console.WriteLine("SoftwareTrigger sample exited.");
        }

        private static void HandleExit()
        {
            if (_exitHandled)
            {
                return;
            }
            _exitHandled = true;

            Console.WriteLine("\nExiting sample application...");
            AskUserSyncModePreference();
        }

        private static void AskUserSyncModePreference()
        {
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("  Exit Options:");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("  Press 'R' to Restore default mode (Standalone) - Default");
            Console.WriteLine("  Press 'K' to Keep software trigger mode");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.Write("  Your choice (R/K, timeout 15s, default=R): ");

            // Wait for user input with timeout, default to Restore (keepMode = false)
            _keepSoftwareTriggerMode = false;
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(15);

            while (DateTime.Now - startTime < timeout)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.R)
                    {
                        Console.WriteLine("R");
                        _keepSoftwareTriggerMode = false;
                        break;
                    }
                    else if (key.Key == ConsoleKey.K)
                    {
                        Console.WriteLine("K");
                        _keepSoftwareTriggerMode = true;
                        break;
                    }
                }
                Thread.Sleep(50);
            }

            if (_keepSoftwareTriggerMode)
            {
                Console.WriteLine("Keeping software trigger mode.");
            }
            else
            {
                Console.WriteLine("Will restore default sync mode (Standalone)...");
            }
        }
    }
}
