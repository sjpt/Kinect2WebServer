using Orbbec;
using Samples.Common;

namespace Samples.Confidence
{
    class Program
    {
        private static volatile bool _isRunning = true;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Confidence Stream Sample - Starting...");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _isRunning = false;
            };

            Pipeline? pipeline = null;
            OrbbecRenderer? renderer = null;
            try
            {
                pipeline = new Pipeline();

                // Check if device supports confidence sensor first
                try
                {
                    using var device = pipeline.GetDevice();
                    using var confidenceSensor = device.GetSensor(SensorType.OB_SENSOR_CONFIDENCE);
                    if (confidenceSensor == null)
                    {
                        Console.WriteLine("This sample requires a device with a confidence sensor.");
                        Console.WriteLine("\nPress any key to exit.");
                        Console.ReadKey(true);
                        return;
                    }
                }
                catch
                {
                    Console.WriteLine("This sample requires a device with a confidence sensor.");
                    Console.WriteLine("\nPress any key to exit.");
                    Console.ReadKey(true);
                    return;
                }

                // Device supports confidence, now create window
                renderer = new OrbbecRenderer(title: "Confidence Stream");
                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                // By creating config to configure which streams to enable or disable for the pipeline, here the depth stream will be enabled.
                using var config = new Config();

                // Enable depth stream first
                config.EnableVideoStream(StreamType.OB_STREAM_DEPTH);

                // Enable confidence stream. The resolution and fps of confidence must match depth stream.
                // Get the enabled stream profile list to retrieve depth stream info
                using var enabledProfiles = config.GetEnabledStreamProfileList();
                if (enabledProfiles != null)
                {
                    for (uint i = 0; i < enabledProfiles.ProfileCount(); i++)
                    {
                        using var profile = enabledProfiles.GetProfile((int)i);
                        if (profile != null && profile.GetStreamType() == StreamType.OB_STREAM_DEPTH)
                        {
                            using var depthProfile = profile.As<VideoStreamProfile>();
                            if (depthProfile != null)
                            {
                                config.EnableVideoStream(StreamType.OB_STREAM_CONFIDENCE,
                                    (int)depthProfile.GetWidth(),
                                    (int)depthProfile.GetHeight(),
                                    (int)depthProfile.GetFPS());
                                Console.WriteLine($"Enabled confidence stream");
                            }
                            break;
                        }
                    }
                }

                pipeline.Start(config);

                // Create textures for rendering
                int depthTextureIndex = renderer.AddVideoFrame();
                int confidenceTextureIndex = renderer.AddVideoFrame();

                _ = Task.Run(() => StartStream(pipeline, renderer, depthTextureIndex, confidenceTextureIndex));

                Console.WriteLine("Pipeline started. Rendering... (Press ESC to close)");
                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                pipeline?.Stop();
                renderer?.Close();
                renderer?.Dispose();
                Console.WriteLine("Confidence sample exited.");
            }
        }

        static void StartStream(Pipeline pipeline, OrbbecRenderer renderer, int depthTextureIndex, int confidenceTextureIndex)
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
                        byte[] data = new byte[depthFrame.GetDataSize()];
                        depthFrame.CopyData(ref data);
                        renderer.UpdateVideoFrame(depthTextureIndex, (int)depthFrame.GetWidth(),
                            (int)depthFrame.GetHeight(), depthFrame.GetFormat(), data, depthFrame);
                    }

                    // Process Confidence Frame
                    using var confidenceFrame = frameSet.GetFrame(FrameType.OB_FRAME_CONFIDENCE);
                    if (confidenceFrame != null)
                    {
                        using var videoFrame = confidenceFrame.As<VideoFrame>();
                        if (videoFrame != null)
                        {
                            byte[] data = new byte[videoFrame.GetDataSize()];
                            videoFrame.CopyData(ref data);
                            renderer.UpdateVideoFrame(confidenceTextureIndex, (int)videoFrame.GetWidth(),
                                (int)videoFrame.GetHeight(), videoFrame.GetFormat(), data, videoFrame);
                        }
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
