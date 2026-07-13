using Orbbec;
using Samples.Common;

namespace Samples.HWD2CAlign
{
    class Program
    {
        private static volatile bool _isRunning = true;
        private static volatile bool _enableAlignMode = true;
        private static volatile float _alpha = 0.6f;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("HW D2C Align - Starting...");

            using var renderer = new OrbbecRenderer(title: "HW D2C Align");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                renderer.Close();
            };

            Pipeline? pipe = null;
            Config? config = null;
            try
            {
                pipe = new Pipeline();
                pipe.EnableFrameSync();

                config = CreateHwD2CAlignConfig(pipe);
                if (config == null)
                {
                    Console.WriteLine("Current device does not support hardware depth-to-color alignment.");
                    return;
                }

                pipe.Start(config);

                int d2cTextureIndex = renderer.AddVideoFrame();
                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                _ = Task.Run(async () => await HandleKeyPress(pipe, config));
                _ = Task.Run(() => StartStream(pipe, renderer, d2cTextureIndex));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                pipe?.Stop();
                config?.Dispose();
                Console.WriteLine("HW D2C Align sample exited.");
            }
        }

        private static Config? CreateHwD2CAlignConfig(Pipeline pipe)
        {
            using var coloStreamProfiles = pipe.GetStreamProfileList(SensorType.OB_SENSOR_COLOR);
            using var depthStreamProfiles = pipe.GetStreamProfileList(SensorType.OB_SENSOR_DEPTH);

            // Iterate through all color and depth stream profiles to find a match for hardware depth-to-color alignment
            uint colorSpCount = coloStreamProfiles.ProfileCount();
            uint depthSpCount = depthStreamProfiles.ProfileCount();
            for (int i = 0; i < colorSpCount; i++)
            {
                using var colorProfile = coloStreamProfiles.GetProfile(i);
                using var colorVsp = colorProfile.As<VideoStreamProfile>();

                for (int j = 0; j < depthSpCount; j++)
                {
                    using var depthProfile = depthStreamProfiles.GetProfile(j);
                    using var depthVsp = depthProfile.As<VideoStreamProfile>();

                    // make sure the color and depth stream have the same fps, due to some models may not support different fps
                    if (colorVsp.GetFPS() != depthVsp.GetFPS() || colorVsp.GetFormat() != Format.OB_FORMAT_RGB || colorVsp.GetFPS() == 90)
                    {
                        continue;
                    }

                    // Check if the given stream profiles support hardware depth-to-color alignment
                    if (CheckIfSupportHWD2CAlign(pipe, colorProfile, depthProfile))
                    {
                        // If support, create a config for hardware depth-to-color alignment
                        var hwD2CAlignConfig = new Config();
                        hwD2CAlignConfig.EnableStream(colorProfile);                                                     // enable color stream
                        hwD2CAlignConfig.EnableStream(depthProfile);                                                     // enable depth stream
                        hwD2CAlignConfig.SetAlignMode(AlignMode.ALIGN_D2C_HW_MODE);                                      // enable hardware depth-to-color alignment
                        hwD2CAlignConfig.SetFrameAggregateOutputMode(FrameAggregateOutputMode.OB_FRAME_AGGREGATE_OUTPUT_ALL_TYPE_FRAME_REQUIRE);  // output frameset with all types of frames
                        return hwD2CAlignConfig;
                    }
                }
            }
            return null;
        }

        private static bool CheckIfSupportHWD2CAlign(Pipeline pipeline, StreamProfile colorStreamProfile, StreamProfile depthStreamProfile)
        {
            using var hwD2CSupportedDepthStreamProfiles = pipeline.GetD2CDepthProfileList(colorStreamProfile, AlignMode.ALIGN_D2C_HW_MODE);
            if (hwD2CSupportedDepthStreamProfiles.ProfileCount() == 0)
                return false;

            // Iterate through the supported depth stream profiles and check if there is a match with the given depth stream profile
            using var depthVsp = depthStreamProfile.As<VideoStreamProfile>();
            int count = (int)hwD2CSupportedDepthStreamProfiles.ProfileCount();
            for (int i = 0; i < count; i++)
            {
                using var sp = hwD2CSupportedDepthStreamProfiles.GetProfile(i);
                using var vsp = sp.As<VideoStreamProfile>();
                if (vsp.GetWidth() == depthVsp.GetWidth() && vsp.GetHeight() == depthVsp.GetHeight() && vsp.GetFormat() == depthVsp.GetFormat()
                    && vsp.GetFPS() == depthVsp.GetFPS())
                {
                    // Found a matching depth stream profile, it is means the given stream profiles support hardware depth-to-color alignment
                    return true;
                }
            }
            return false;
        }

        private static async Task HandleKeyPress(Pipeline pipeline, Config config)
        {
            Console.WriteLine("'T': Enable/Disable HwD2C, '+/-': Adjust Transparency");
            while (_isRunning)
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(50);
                    continue;
                }

                var keyInfo = Console.ReadKey(true);
                if (keyInfo.Key == ConsoleKey.T)
                {
                    _enableAlignMode = !_enableAlignMode;

                    try
                    {
                        if (_enableAlignMode)
                        {
                            config.SetAlignMode(AlignMode.ALIGN_D2C_HW_MODE);
                            Console.WriteLine("Haeware Depth to Color Align: Enabled");
                        }
                        else
                        {
                            config.SetAlignMode(AlignMode.ALIGN_DISABLE);
                            Console.WriteLine("Haeware Depth to Color Align: Disabled");
                        }

                        pipeline.Stop();
                        pipeline.Start(config);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Unexpected error: {ex.Message}");
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Add || keyInfo.Key == ConsoleKey.OemPlus)
                {
                    _alpha = Math.Min(_alpha + 0.1f, 1.0f);
                    Console.WriteLine($"Adjust alpha to {_alpha:F2}");
                }
                else if (keyInfo.Key == ConsoleKey.Subtract || keyInfo.Key == ConsoleKey.OemMinus)
                {
                    _alpha = Math.Max(_alpha - 0.1f, 0.0f);
                    Console.WriteLine($"Adjust alpha to {_alpha:F2}");
                }
            }
        }

        private static void StartStream(Pipeline pipeline, OrbbecRenderer renderer, int d2cTextureIndex)
        {
            try
            {
                while (_isRunning)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    using var colorFrame = frameSet?.GetColorFrame();
                    using var depthFrame = frameSet?.GetDepthFrame();

                    if (colorFrame == null || depthFrame == null)
                        continue;

                    byte[] d2cData = new byte[colorFrame.GetDataSize()];
                    DepthOverlayColorProcess(colorFrame, depthFrame, ref d2cData);
                    renderer.UpdateVideoFrame(d2cTextureIndex, (int)colorFrame.GetWidth(),
                        (int)colorFrame.GetHeight(), Format.OB_FORMAT_RGB, d2cData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Start stream error: {ex.Message}");
            }
        }

        private static void DepthOverlayColorProcess(VideoFrame colorFrame, VideoFrame depthFrame, ref byte[] d2cData)
        {
            try
            {
                int colorWidth = (int)colorFrame.GetWidth();
                int colorHeight = (int)colorFrame.GetHeight();
                byte[] colorData = new byte[colorFrame.GetDataSize()];
                colorFrame.CopyData(ref colorData);

                // Use depth frame's own dimensions for Y16ToRgb conversion
                int depthWidth = (int)depthFrame.GetWidth();
                int depthHeight = (int)depthFrame.GetHeight();
                byte[] depthDataRaw = new byte[depthFrame.GetDataSize()];
                depthFrame.CopyData(ref depthDataRaw);
                byte[] depthData = ImageUtils.Y16ToRgb(depthWidth, depthHeight, depthDataRaw);

                // Call the overload that handles different sizes
                d2cData = ImageUtils.DepthAlignToColor(colorWidth, colorHeight, colorData,
                    depthWidth, depthHeight, depthData, _alpha);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DepthOverlayColorProcess error: {ex.Message}");
            }
        }
    }
}