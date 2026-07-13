using System.Runtime.InteropServices;
using Orbbec;
using Samples.Common;

namespace Samples.PostProcessing
{
    class Program
    {
        private static volatile bool _isRunning = true;
        private static volatile bool _quitProgram = false;
        private static volatile bool _captureRequested = false;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Post Processing - Starting...");

            using var renderer = new OrbbecRenderer(title: "Post Processing");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                renderer.Close();
            };

            Pipeline? pipe = null;
            Device? device = null;
            Config? config = null;
            try
            {
                pipe = new Pipeline();
                device = pipe.GetDevice();
                using var sensor = device.GetSensor(SensorType.OB_SENSOR_DEPTH);
                var filterList = sensor.CreateRecommendedFilters();

                PrintFiltersInfo(filterList);

                config = new Config();
                config.EnableStream(StreamType.OB_STREAM_DEPTH);

                pipe.Start(config);

                int depthTextureIndex = renderer.AddVideoFrame();
                int processedTextureIndex = renderer.AddVideoFrame();
                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                _ = Task.Run(() => StartStream(pipe, filterList, renderer, depthTextureIndex, processedTextureIndex));
                _ = Task.Run(() => FilterControl(filterList, renderer));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                _quitProgram = true;
                pipe?.Stop();
                config?.Dispose();
                device?.Dispose();
                Console.WriteLine("Post Processing sample exited.");
            }
        }

        private static void PrintFiltersInfo(List<Filter> filterList)
        {
            Console.WriteLine($"{filterList.Count} post processing filters recommended:");
            foreach (var filter in filterList)
            {
                Console.WriteLine($" - {filter.Name()}: {(filter.IsEnabled() ? "enabled" : "disabled")}");
                var configSchemaList = filter.GetConfigSchemaList();

                // Print the config schema for each filter
                foreach (var configSchema in configSchemaList)
                {
                    Console.WriteLine($"    - {{{Marshal.PtrToStringAnsi(configSchema.name)}, {configSchema.type}, " +
                                            $"{configSchema.min}, {configSchema.max}, {configSchema.step}, {configSchema.def}, " +
                                            $"{Marshal.PtrToStringAnsi(configSchema.desc)} }}");
                }
                filter.Enable(false);  // Disable the filter
            }
        }

        private static void FilterControl(List<Filter> filterList, OrbbecRenderer renderer)
        {
            PrintHelp();

            while (!_quitProgram)
            {
                Console.WriteLine("---------------------------");
                Console.Write("Enter your input (h for help): ");

                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.Equals("q", StringComparison.OrdinalIgnoreCase))
                {
                    _quitProgram = true;
                    _isRunning = false;
                    renderer.Close();
                    break;
                }
                else if (input.Equals("l", StringComparison.OrdinalIgnoreCase))
                {
                    PrintFiltersInfo(filterList);
                    continue;
                }
                else if (input.Equals("h", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelp();
                    continue;
                }
                else if (input.Equals("c", StringComparison.OrdinalIgnoreCase))
                {
                    _captureRequested = true;
                    Console.WriteLine("Capture requested, saving depth frames to Output/Processing/ ...");
                    continue;
                }

                string[] tokens = input.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;

                bool foundFilter = false;
                foreach (var filter in filterList)
                {
                    if (filter.Name() != tokens[0]) continue;

                    foundFilter = true;
                    if (tokens.Length == 1)
                    {  // print list of configs for the filter
                        var configSchemaList = filter.GetConfigSchemaList();
                        Console.WriteLine($"Config values for {filter.Name()}:");
                        foreach (var configSchema in configSchemaList)
                        {
                            string name = Marshal.PtrToStringAnsi(configSchema.name) ?? string.Empty;
                            if (!string.IsNullOrEmpty(name))
                            {
                                Console.WriteLine($" - {name}: {filter.GetConfigValue(name)}");
                            }
                        }
                    }
                    else if (tokens.Length == 2 && (tokens[1] == "on" || tokens[1] == "off"))
                    {  // Enable/disable the filter
                        filter.Enable(tokens[1] == "on");
                        Console.WriteLine($"Success: Filter {filter.Name()} is now {(filter.IsEnabled() ? "enabled" : "disabled")}");
                    }
                    else if (tokens.Length == 2 && tokens[1] == "list")
                    {  // List the config values for the filter
                        var configSchemaList = filter.GetConfigSchemaList();
                        Console.WriteLine($"Config schema for {filter.Name()}:");
                        foreach (var configSchema in configSchemaList)
                        {
                            Console.WriteLine($" - {{{Marshal.PtrToStringAnsi(configSchema.name)}, {configSchema.type}, " +
                                    $"{configSchema.min}, {configSchema.max}, {configSchema.step}, {configSchema.def}, " +
                                    $"{Marshal.PtrToStringAnsi(configSchema.desc)} }}");
                        }
                    }
                    else if (tokens.Length == 2)
                    {  // Print the config schema for the filter
                        var configSchemaList = filter.GetConfigSchemaList();
                        bool foundConfig = false;
                        foreach (var configSchema in configSchemaList)
                        {
                            string name = Marshal.PtrToStringAnsi(configSchema.name) ?? string.Empty;
                            if (!string.IsNullOrEmpty(name) && name.Equals(tokens[1], StringComparison.OrdinalIgnoreCase))
                            {
                                foundConfig = true;
                                Console.WriteLine($"Config values for {filter.Name()}@{name}: {filter.GetConfigValue(name)}");
                                break;
                            }
                        }
                        if (!foundConfig)
                        {
                            Console.WriteLine($"Error: Config {tokens[1]} not found for filter {filter.Name()}");
                        }
                    }
                    else if (tokens.Length == 3)
                    {  // Set a config value
                        try
                        {
                            double value = double.Parse(tokens[2]);
                            filter.SetConfigValue(tokens[1], value);
                            Console.WriteLine($"Success: Config value of {tokens[1]} for filter {filter.Name()} is set to {tokens[2]}");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Error: {e.Message}");
                            continue;
                        }
                    }
                    break;
                }
                if (!foundFilter)
                {
                    Console.WriteLine($"Error: Filter {tokens[0]} not found");
                }
                Thread.Sleep(500);
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("- Enter `[Filter]` to list the config values for the filter");
            Console.WriteLine("- Enter `[Filter] on` or `[Filter] off` to enable/disable the filter");
            Console.WriteLine("- Enter `[Filter] list` to list the config schema for the filter");
            Console.WriteLine("- Enter `[Filter] [Config]` to show the config values for the filter");
            Console.WriteLine("- Enter `[Filter] [Config] [Value]` to set a config value");
            Console.WriteLine("- Enter `L`or `l` to list all available filters");
            Console.WriteLine("- Enter `H` or `h` to print this help message");
            Console.WriteLine("- Enter `C` or `c` to capture depth frames (before/after processing) as .raw files");
            Console.WriteLine("- Enter `Q` or `q` to quit");
        }

        private static void StartStream(Pipeline pipeline, List<Filter> filterList, OrbbecRenderer renderer, int depthTextureIndex, int processedTextureIndex)
        {
            try
            {
                while (_isRunning)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    using var depthFrameRaw = frameSet.GetFrame(FrameType.OB_FRAME_DEPTH);
                    if (depthFrameRaw == null)
                        continue;

                    Frame? processedFrame = null;

                    // Apply the recommended filters to the depth frame
                    foreach (var filter in filterList)
                    {
                        var frameToProcess = processedFrame ?? depthFrameRaw;
                        if (filter.IsEnabled())
                        {  // Only apply enabled filters
                            var newProcessedFrame = filter.Process(frameToProcess);
                            if (newProcessedFrame != null)
                            {
                                // Dispose the previous processed frame and use the new one
                                processedFrame?.Dispose();
                                processedFrame = newProcessedFrame;
                            }
                            else
                            {
                                // Filter.Process returned null, stop processing and use the last valid frame
                                Console.WriteLine($"Warning: Filter {filter.Name()} returned null, using last valid frame");
                                break;
                            }
                        }
                    }

                    // Check if a capture was requested
                    if (_captureRequested)
                    {
                        _captureRequested = false;
                        string outDir = "Output/Process";
                        Directory.CreateDirectory(outDir);

                        using var depthFrame = depthFrameRaw.As<DepthFrame>();
                        var frameForCapture = processedFrame ?? depthFrameRaw;
                        using var processedDepthFrame = frameForCapture.As<DepthFrame>();
                        if (depthFrame != null && processedDepthFrame != null)
                        {
                            SaveDepthRaw($"{outDir}/depth_before_{depthFrame.GetWidth()}x{depthFrame.GetHeight()}_{depthFrame.GetTimeStamp()}.raw", depthFrame);
                            SaveDepthRaw($"{outDir}/depth_after_{processedDepthFrame.GetWidth()}x{processedDepthFrame.GetHeight()}_{processedDepthFrame.GetTimeStamp()}.raw", processedDepthFrame);
                        }
                    }

                    // Render original depth frame
                    var depthFrameForRender = depthFrameRaw.As<DepthFrame>();
                    if (depthFrameForRender != null)
                    {
                        using (depthFrameForRender)
                        {
                            byte[] depthData = new byte[depthFrameForRender.GetDataSize()];
                            depthFrameForRender.CopyData(ref depthData);
                            renderer.UpdateVideoFrame(depthTextureIndex, (int)depthFrameForRender.GetWidth(),
                                (int)depthFrameForRender.GetHeight(), Format.OB_FORMAT_Y16, depthData);
                        }
                    }

                    // Render processed frame
                    var frameToRender = processedFrame ?? depthFrameRaw;
                    var processedFrameForRender = frameToRender.As<DepthFrame>();
                    if (processedFrameForRender != null)
                    {
                        using (processedFrameForRender)
                        {
                            byte[] resultData = new byte[processedFrameForRender.GetDataSize()];
                            processedFrameForRender.CopyData(ref resultData);
                            renderer.UpdateVideoFrame(processedTextureIndex, (int)processedFrameForRender.GetWidth(),
                                (int)processedFrameForRender.GetHeight(), Format.OB_FORMAT_Y16, resultData);
                        }
                    }

                    // Only dispose processedFrame if it's different from depthFrameRaw
                    // Since As<T>() now shares the same handle reference, we don't need to worry about double-free
                    processedFrame?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Start stream error: {ex.Message}");
            }
        }

        private static void SaveDepthRaw(string filePath, Frame frame)
        {
            if (frame == null)
            {
                Console.WriteLine("SaveDepthRaw: null frame, skip.");
                return;
            }

            try
            {
                byte[] data = new byte[frame.GetDataSize()];
                frame.CopyData(ref data);
                File.WriteAllBytes(filePath, data);
                Console.WriteLine($"Saved: {filePath} ({data.Length} bytes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SaveDepthRaw: failed to save {filePath} - {ex.Message}");
            }
        }
    }
}
