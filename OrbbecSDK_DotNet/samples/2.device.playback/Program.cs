using Orbbec;
using Samples.Common;

namespace Samples.Playback
{
    class Program
    {
        private static volatile bool _isRunning = true;
        private static volatile bool _isExited = false;
        private static PlaybackDevice? _playback = null;
        private static Pipeline? _pipe = null;
        private static readonly object _frameLock = new();
        private static readonly object _playbackLock = new();
        private static Frameset? _renderFrameset = null;
        private static PlaybackStatus _playStatus = PlaybackStatus.OB_PLAYBACK_UNKNOWN;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Playback - Starting...");

            bool result = GetRosbagPath(out string filePath);
            if (!result) return;

            using var renderer = new OrbbecRenderer(title: "Playback");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                renderer.Close();
            };

            try
            {
                // Create playback device
                _playback = new PlaybackDevice(filePath);

                // Create pipeline with playback device
                _pipe = new Pipeline(_playback);

                // Create config
                using var config = new Config();

                // Print duration info (safe to call before pipeline starts)
                try
                {
                    var duration = _playback.GetDuration();
                    Console.WriteLine($"Duration: {FormatTime(duration)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting duration: {ex.Message}");
                }

                // IMPORTANT: Set frame aggregate output mode for playback
                config.SetFrameAggregateOutputMode(FrameAggregateOutputMode.OB_FRAME_AGGREGATE_OUTPUT_ANY_SITUATION);

                // Set playback status change callback
                _playback.SetPlaybackStatusChangeCallback(status =>
                {
                    lock (_playbackLock)
                    {
                        _playStatus = status;
                    }
                    if (status == PlaybackStatus.OB_PLAYBACK_STOPPED)
                    {
                        Console.WriteLine("Playback stopped - will replay in 1 second");
                    }
                });

                // Enable all streams from the playback device
                using var sensorList = _playback.GetSensorList();
                for (uint i = 0; i < sensorList.SensorCount(); ++i)
                {
                    var sensorType = sensorList.SensorType(i);
                    config.EnableStream(sensorType);
                    Console.WriteLine($"Enabled stream for: {sensorType}");
                }

                // Start pipeline with callback (like C++ example)
                _pipe.Start(config, OnFrameset);

                // Now safe to print full playback info
                PrintPlaybackInfo();

                // Add video frames for rendering
                var textureIndices = new Dictionary<SensorType, int>();
                for (uint i = 0; i < sensorList.SensorCount(); ++i)
                {
                    var sensorType = sensorList.SensorType(i);
                    if (sensorType == SensorType.OB_SENSOR_ACCEL || sensorType == SensorType.OB_SENSOR_GYRO)
                        continue;

                    textureIndices.Add(sensorType, renderer.AddVideoFrame());
                }

                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                    _isExited = true;
                };

                // Start replay monitor thread
                _ = Task.Run(() => ReplayMonitor(config));

                // Start command processing task
                _ = Task.Run(() => CommandLoop(renderer));

                // Print usage
                PrintUsage();

                // Start render thread
                _ = Task.Run(() => RenderFrames(renderer, textureIndices));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                _isExited = true;
                _playback?.SetPlaybackStatusChangeCallback(null);
                lock (_playbackLock)
                {
                    _pipe?.Stop();
                }
                lock (_frameLock)
                {
                    _renderFrameset?.Dispose();
                    _renderFrameset = null;
                }
                _pipe?.Dispose();
                _playback?.Dispose();
                Console.WriteLine("Playback sample exited.");
            }
        }

        static void OnFrameset(Frameset frameset)
        {
            // Process IMU frames (print to console)
            ProcessImuFrames(frameset);

            lock (_frameLock)
            {
                _renderFrameset?.Dispose();
                _renderFrameset = frameset;
            }
        }

        static void ProcessImuFrames(Frameset frameSet)
        {
            // Process Accel frame
            using var accelFrameRaw = frameSet.GetFrame(FrameType.OB_FRAME_ACCEL);
            if (accelFrameRaw != null)
            {
                using var accelFrame = accelFrameRaw.As<AccelFrame>();
                var accelIndex = accelFrame.GetIndex();
                var accelTimeStampUs = accelFrame.GetTimeStampUs();
                var accelTemperature = accelFrame.GetTemperature();
                var accelType = accelFrame.GetFrameType();
                // Print every 50 frames to avoid console flooding
                if (accelIndex % 50 == 0)
                {
                    var accelValue = accelFrame.GetAccelValue();
                    var obFloat3d = new Float3D { x = accelValue.x, y = accelValue.y, z = accelValue.z };
                    PrintImuValue(obFloat3d, accelIndex, accelTimeStampUs, accelTemperature, accelType, "m/s^2");
                }
            }

            // Process Gyro frame
            using var gyroFrameRaw = frameSet.GetFrame(FrameType.OB_FRAME_GYRO);
            if (gyroFrameRaw != null)
            {
                using var gyroFrame = gyroFrameRaw.As<GyroFrame>();
                var gyroIndex = gyroFrame.GetIndex();
                var gyroTimeStampUs = gyroFrame.GetTimeStampUs();
                var gyroTemperature = gyroFrame.GetTemperature();
                var gyroType = gyroFrame.GetFrameType();
                // Print every 50 frames to avoid console flooding
                if (gyroIndex % 50 == 0)
                {
                    var gyroValue = gyroFrame.GetGyroValue();
                    var obFloat3d = new Float3D { x = gyroValue.x, y = gyroValue.y, z = gyroValue.z };
                    PrintImuValue(obFloat3d, gyroIndex, gyroTimeStampUs, gyroTemperature, gyroType, "rad/s");
                }
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

        static void ReplayMonitor(Config config)
        {
            while (!_isExited && _isRunning)
            {
                PlaybackStatus status;
                lock (_playbackLock)
                {
                    status = _playStatus;
                }

                if (status == PlaybackStatus.OB_PLAYBACK_STOPPED)
                {
                    try
                    {
                        lock (_playbackLock)
                        {
                            if (_isExited || !_isRunning) break;
                            _pipe?.Stop();
                        }

                        // Wait 1 second before replaying
                        Thread.Sleep(1000);

                        if (_isExited || !_isRunning) break;

                        lock (_playbackLock)
                        {
                            _playStatus = PlaybackStatus.OB_PLAYBACK_UNKNOWN;
                            _pipe?.Start(config, OnFrameset);
                            Console.WriteLine("Replay started");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Replay error: {ex.Message}");
                    }
                }

                Thread.Sleep(100);
            }
        }

        static void RenderFrames(OrbbecRenderer renderer, Dictionary<SensorType, int> textureIndices)
        {
            while (_isRunning)
            {
                Frameset? frameSet = null;
                lock (_frameLock)
                {
                    if (_renderFrameset != null)
                    {
                        // Note: We don't dispose here since the frameset is owned by the callback
                        // In a real implementation, we might need to clone the frameset
                        frameSet = _renderFrameset;
                    }
                }

                if (frameSet != null)
                {
                    ProcessVideoFrames(frameSet, renderer, textureIndices);
                }

                Thread.Sleep(33); // ~30 FPS
            }
        }

        private static void ProcessVideoFrames(Frameset frameSet, OrbbecRenderer renderer, Dictionary<SensorType, int> textureIndices)
        {
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

                if (frameType == FrameType.OB_FRAME_UNKNOWN)
                    continue;

                using var frame = frameSet.GetFrame(frameType);

                if (frame != null)
                {
                    if (frameType == FrameType.OB_FRAME_CONFIDENCE)
                    {
                        using var depthFrame = frameSet.GetFrame(FrameType.OB_FRAME_DEPTH)?.As<VideoFrame>();
                        if (depthFrame == null)
                        {
                            continue;
                        }
                        byte[] data = new byte[frame.GetDataSize()];
                        frame.CopyData(ref data);
                        renderer.UpdateVideoFrame(textureIndex, (int)depthFrame.GetWidth(), (int)depthFrame.GetHeight(), Format.OB_FORMAT_Y8, data);
                    }
                    else
                    {
                        using var vf = frame.As<VideoFrame>();
                        var format = vf.GetFormat();
                        byte[] data = new byte[vf.GetDataSize()];
                        vf.CopyData(ref data);

                        // Check if format needs SDK filter conversion (MJPG, YUYV, etc.)
                        bool needsFilterConversion = format == Format.OB_FORMAT_MJPG ||
                                                      format == Format.OB_FORMAT_YUYV ||
                                                      format == Format.OB_FORMAT_YUY2 ||
                                                      format == Format.OB_FORMAT_UYVY ||
                                                      format == Format.OB_FORMAT_NV12 ||
                                                      format == Format.OB_FORMAT_NV21 ||
                                                      format == Format.OB_FORMAT_I420 ||
                                                      format == Format.OB_FORMAT_BGR ||
                                                      format == Format.OB_FORMAT_RGBA;

                        // Pass frame to support formats requiring Filter conversion
                        renderer.UpdateVideoFrame(textureIndex, (int)vf.GetWidth(), (int)vf.GetHeight(), format, data, frame);
                    }
                }
            }
        }

        private static void PrintPlaybackInfo()
        {
            if (_playback == null) return;

            try
            {
                var duration = _playback.GetDuration();
                var position = _playback.GetPosition();
                // REMOVED: GetCurrentPlaybackStatus() causes AccessViolationException - SDK bug
                // var status = _playback.GetCurrentPlaybackStatus();

                Console.WriteLine($"\n=== Playback Info ===");
                Console.WriteLine($"  Duration: {FormatTime(duration)}");
                Console.WriteLine($"  Position: {FormatTime(position)}");
                // REMOVED: Console.WriteLine($"  Status: {status}");
                Console.WriteLine($"=====================\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting playback info: {ex.Message}");
            }
        }

        private static string FormatTime(UInt64 milliseconds)
        {
            var time = TimeSpan.FromMilliseconds(milliseconds);
            return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
        }

        private static void CommandLoop(OrbbecRenderer renderer)
        {
            while (_isRunning)
            {
                try
                {
                    Console.Write("\nCommand: ");
                    var cmd = Console.ReadLine()?.Trim().ToLower();
                    if (string.IsNullOrEmpty(cmd))
                        continue;

                    if (cmd == "q" || cmd == "quit")
                    {
                        _isRunning = false;
                        _isExited = true;
                        renderer.Close();
                        break;
                    }

                    lock (_playbackLock)
                    {
                        if (_playback == null)
                        {
                            Console.WriteLine("Playback not available");
                            continue;
                        }

                        CommandProcess(cmd);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Command error: {ex.Message}");
                }
            }
        }

        private static void CommandProcess(string cmd)
        {
            if (_playback == null) return;

            try
            {
                switch (cmd)
                {
                    case "pause":
                    case "p":
                        _playback.Pause();
                        Console.WriteLine("Playback paused");
                        break;

                    case "resume":
                    case "r":
                        _playback.Resume();
                        Console.WriteLine("Playback resumed");
                        break;

                    case "seek":
                        Console.Write("Enter seek position (seconds): ");
                        if (uint.TryParse(Console.ReadLine(), out uint seconds))
                        {
                            _playback.Seek((ulong)seconds * 1000);
                            Console.WriteLine($"Seeked to {seconds} seconds");
                        }
                        else
                        {
                            Console.WriteLine("Invalid input");
                        }
                        break;

                    case "forward":
                    case "f":
                        {
                            var currentPos = _playback.GetPosition();
                            var duration = _playback.GetDuration();
                            var newPos = Math.Min(currentPos + 5000, duration); // +5 seconds
                            _playback.Seek(newPos);
                            Console.WriteLine($"Forwarded to {FormatTime(newPos)}");
                        }
                        break;

                    case "backward":
                    case "b":
                        {
                            var currentPos = _playback.GetPosition();
                            var newPos = currentPos > 5000 ? currentPos - 5000 : 0; // -5 seconds
                            _playback.Seek(newPos);
                            Console.WriteLine($"Backward to {FormatTime(newPos)}");
                        }
                        break;

                    case "speed":
                    case "rate":
                        Console.Write("Enter playback rate (0.5, 1.0, 2.0, etc.): ");
                        if (float.TryParse(Console.ReadLine(), out float rate))
                        {
                            _playback.SetPlaybackRate(rate);
                            Console.WriteLine($"Playback rate set to {rate}x");
                        }
                        else
                        {
                            Console.WriteLine("Invalid input");
                        }
                        break;

                    case "info":
                    case "i":
                        PrintPlaybackInfo();
                        break;

                    case "pos":
                    case "position":
                        {
                            var pos = _playback.GetPosition();
                            var duration = _playback.GetDuration();
                            var percentage = duration > 0 ? (pos * 100.0 / duration) : 0;
                            Console.WriteLine($"Position: {FormatTime(pos)} / {FormatTime(duration)} ({percentage:F1}%)");
                        }
                        break;

                    case "help":
                    case "h":
                    case "?":
                        PrintUsage();
                        break;

                    default:
                        Console.WriteLine("Unknown command. Type 'help' for available commands.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Command error: {ex.Message}");
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("\n=== Playback Commands ===");
            Console.WriteLine("  pause / p           - Pause playback");
            Console.WriteLine("  resume / r          - Resume playback");
            Console.WriteLine("  seek                - Seek to specified position (in seconds)");
            Console.WriteLine("  forward / f         - Forward 5 seconds");
            Console.WriteLine("  backward / b        - Backward 5 seconds");
            Console.WriteLine("  speed / rate        - Set playback rate (0.5, 1.0, 2.0, etc.)");
            Console.WriteLine("  info / i            - Show playback information");
            Console.WriteLine("  position / pos      - Show current position");
            Console.WriteLine("  help / h / ?        - Show this help");
            Console.WriteLine("  quit / q            - Quit application");
            Console.WriteLine("=========================\n");
        }

        private static bool GetRosbagPath(out string rosbagPath)
        {
            rosbagPath = string.Empty;

            while (_isRunning)
            {
                Console.WriteLine("Please input the path of the Rosbag file (.bag) to playback:");
                Console.Write("Path: ");

                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                input = input.Trim('\'', '"');
                if (input.EndsWith(".bag", StringComparison.OrdinalIgnoreCase))
                {
                    rosbagPath = input;
                    Console.WriteLine($"Playback file confirmed: {rosbagPath}\n");
                    return true;
                }

                Console.Write("Invalid file format. Please provide a .bag file.\n");
            }

            return false;
        }
    }
}
