using Orbbec;
using Samples.Common;

namespace Samples.LaserInterleave
{
    class Program
    {
        private static volatile bool _isRunning = true;
        private static Pipeline? _pipeline = null;
        private static Device? _device = null;
        private static Filter? _postDepthFilter = null;
        private static Filter? _postLeftInfraredFilter = null;
        private static Filter? _postRightInfraredFilter = null;
        private static OrbbecRenderer? _renderer = null;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Laser Interleave Sample - Starting...");

            using var renderer = new OrbbecRenderer(title: "Laser On-Off");
            _renderer = renderer;

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _isRunning = false;
                renderer.Close();
            };

            try
            {
                _pipeline = new Pipeline();
                _device = _pipeline.GetDevice();

                // Check if device supports frame interleave
                if (!_device.IsFrameInterleaveSupported())
                {
                    Console.WriteLine("Current device does not support frame interleave");
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    return;
                }

                // Configure streams
                using var config = new Config();
                config.EnableVideoStream(StreamType.OB_STREAM_DEPTH);
                config.EnableVideoStream(StreamType.OB_STREAM_IR_LEFT);
                config.EnableVideoStream(StreamType.OB_STREAM_IR_RIGHT);
                config.SetFrameAggregateOutputMode(FrameAggregateOutputMode.OB_FRAME_AGGREGATE_OUTPUT_ALL_TYPE_FRAME_REQUIRE);

                // Create SequenceIdFilter for each stream
                _postDepthFilter = FilterFactory.CreateFilter("SequenceIdFilter");
                _postLeftInfraredFilter = FilterFactory.CreateFilter("SequenceIdFilter");
                _postRightInfraredFilter = FilterFactory.CreateFilter("SequenceIdFilter");

                // Load frame interleave mode as 'Laser On-Off'
                _device.LoadFrameInterleave("Laser On-Off");
                // Enable frame interleave
                _device.SetBoolProperty(PropertyId.OB_PROP_FRAME_INTERLEAVE_ENABLE_BOOL, true);

                // Start pipeline
                _pipeline.Start(config);

                // Set default sequenceid to -1 (all)
                _postDepthFilter.SetConfigValue("sequenceid", -1);
                _postLeftInfraredFilter.SetConfigValue("sequenceid", -1);
                _postRightInfraredFilter.SetConfigValue("sequenceid", -1);

                // Add video frames for rendering
                int depthIndex = renderer.AddVideoFrame();
                int leftIrIndex = renderer.AddVideoFrame();
                int rightIrIndex = renderer.AddVideoFrame();

                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                // Start input watcher thread
                _ = Task.Run(() => InputWatcher());

                // Print initial command tips
                PrintCommandTips();

                // Start stream processing
                _ = Task.Run(() => StartStream(depthIndex, leftIrIndex, rightIrIndex));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                // Cleanup
                _postDepthFilter?.Dispose();
                _postLeftInfraredFilter?.Dispose();
                _postRightInfraredFilter?.Dispose();

                // Disable frame interleave
                try
                {
                    _device?.SetBoolProperty(PropertyId.OB_PROP_FRAME_INTERLEAVE_ENABLE_BOOL, false);
                }
                catch { }

                _pipeline?.Stop();
                _pipeline?.Dispose();
                _device?.Dispose();
                Console.WriteLine("Laser Interleave sample exited.");
            }
        }

        static void PrintCommandTips()
        {
            Console.WriteLine("\n-------------------------------");
            Console.WriteLine("Command usage: <filter> <param>");
            Console.WriteLine("  <filter>: stream filter name, must be one of the following values:");
            Console.WriteLine("            depth");
            Console.WriteLine("            left_ir");
            Console.WriteLine("            right_ir");
            Console.WriteLine("  <param>:  stream filter param, must be one of the following values:");
            Console.WriteLine("            all: disable sequenceid filter");
            Console.WriteLine("            0: set sequenceid to 0");
            Console.WriteLine("            1: set sequenceid to 1");
            Console.WriteLine("Press 'q' or 'quit' to exit the program.");
        }

        static void InputWatcher()
        {
            while (_isRunning)
            {
                try
                {
                    var cmd = Console.ReadLine();
                    if (cmd == null) continue;

                    cmd = cmd.Trim();

                    if (cmd == "quit" || cmd == "q")
                    {
                        _isRunning = false;
                        _renderer?.Close();
                        break;
                    }
                    else
                    {
                        var parts = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length != 2)
                        {
                            Console.WriteLine("Error: invalid param.");
                            PrintCommandTips();
                            continue;
                        }

                        // Get filter
                        Filter? filter = null;
                        if (parts[0] == "depth")
                        {
                            filter = _postDepthFilter;
                        }
                        else if (parts[0] == "left_ir")
                        {
                            filter = _postLeftInfraredFilter;
                        }
                        else if (parts[0] == "right_ir")
                        {
                            filter = _postRightInfraredFilter;
                        }
                        else
                        {
                            Console.WriteLine("Error: invalid filter name.");
                            PrintCommandTips();
                            continue;
                        }

                        // Parse param
                        int sequenceId;
                        if (parts[1] == "all")
                        {
                            sequenceId = -1;
                        }
                        else if (parts[1] == "0")
                        {
                            sequenceId = 0;
                        }
                        else if (parts[1] == "1")
                        {
                            sequenceId = 1;
                        }
                        else
                        {
                            Console.WriteLine("Error: invalid param value.");
                            PrintCommandTips();
                            continue;
                        }

                        // Set filter config
                        try
                        {
                            filter?.SetConfigValue("sequenceid", sequenceId);
                            Console.WriteLine($"Set {parts[0]} sequenceid to {parts[1]} successfully");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Set sequenceid error: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Input error: {ex.Message}");
                }
            }
        }

        static void StartStream(int depthIndex, int leftIrIndex, int rightIrIndex)
        {
            try
            {
                while (_isRunning && _pipeline != null)
                {
                    using var frameSet = _pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    try
                    {
                        // Process depth frame
                        using var depthFrame = frameSet.GetFrame(FrameType.OB_FRAME_DEPTH);
                        if (depthFrame != null && _postDepthFilter != null)
                        {
                            using var processedFrame = _postDepthFilter.Process(depthFrame);
                            if (processedFrame != null)
                            {
                                using var videoFrame = processedFrame.As<VideoFrame>();
                                if (videoFrame != null)
                                {
                                    byte[] data = new byte[videoFrame.GetDataSize()];
                                    videoFrame.CopyData(ref data);
                                    _renderer?.UpdateVideoFrame(depthIndex, (int)videoFrame.GetWidth(),
                                        (int)videoFrame.GetHeight(), videoFrame.GetFormat(), data);
                                }
                            }
                        }

                        // Process left IR frame
                        using var leftIrFrame = frameSet.GetFrame(FrameType.OB_FRAME_IR_LEFT);
                        if (leftIrFrame != null && _postLeftInfraredFilter != null)
                        {
                            using var processedFrame = _postLeftInfraredFilter.Process(leftIrFrame);
                            if (processedFrame != null)
                            {
                                using var videoFrame = processedFrame.As<VideoFrame>();
                                if (videoFrame != null)
                                {
                                    byte[] data = new byte[videoFrame.GetDataSize()];
                                    videoFrame.CopyData(ref data);
                                    _renderer?.UpdateVideoFrame(leftIrIndex, (int)videoFrame.GetWidth(),
                                        (int)videoFrame.GetHeight(), videoFrame.GetFormat(), data);
                                }
                            }
                        }

                        // Process right IR frame
                        using var rightIrFrame = frameSet.GetFrame(FrameType.OB_FRAME_IR_RIGHT);
                        if (rightIrFrame != null && _postRightInfraredFilter != null)
                        {
                            using var processedFrame = _postRightInfraredFilter.Process(rightIrFrame);
                            if (processedFrame != null)
                            {
                                using var videoFrame = processedFrame.As<VideoFrame>();
                                if (videoFrame != null)
                                {
                                    byte[] data = new byte[videoFrame.GetDataSize()];
                                    videoFrame.CopyData(ref data);
                                    _renderer?.UpdateVideoFrame(rightIrIndex, (int)videoFrame.GetWidth(),
                                        (int)videoFrame.GetHeight(), videoFrame.GetFormat(), data);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"SequenceIdFilter error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Stream error: {ex.Message}");
            }
        }
    }
}
