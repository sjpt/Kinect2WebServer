using Orbbec;
using Samples.Common;

namespace Samples.QuickStart
{
    class Program
    {
        private static volatile bool _isRunning = true;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Qucik Start - Starting...");

            using var renderer = new OrbbecRenderer(title: "Quick Start");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                renderer.Close();
            };

            Pipeline? pipe = null;
            try
            {
                pipe = new Pipeline();
                using var config = new Config();

                using var colorProfileList = pipe.GetStreamProfileList(SensorType.OB_SENSOR_COLOR);
                using var colorProfile = colorProfileList.GetVideoStreamProfile(0, 0, Format.OB_FORMAT_RGB, 0);
                Console.WriteLine($"Color Profile: {colorProfile.GetWidth()}x{colorProfile.GetHeight()}@{colorProfile.GetFormat()}");

                using var depthProfileList = pipe.GetStreamProfileList(SensorType.OB_SENSOR_DEPTH);
                using var depthProfile = depthProfileList.GetVideoStreamProfile(0, 0, Format.OB_FORMAT_UNKNOWN, 0);
                Console.WriteLine($"Depth Profile: {depthProfile.GetWidth()}x{depthProfile.GetHeight()}@{depthProfile.GetFormat()}");

                config.EnableStream(colorProfile);
                config.EnableStream(depthProfile);
                pipe.Start(config);

                int colorTextureIndex = renderer.AddVideoFrame();
                int depthTextureIndex = renderer.AddVideoFrame();
                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                _ = Task.Run(() => StartStream(pipe, renderer, colorTextureIndex, depthTextureIndex));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                pipe?.Stop();
                Console.WriteLine("QuickStart sample exited.");
            }
        }

        private static void StartStream(Pipeline pipeline, OrbbecRenderer renderer, int colorTextureIndex, int depthTextureIndex)
        {
            try
            {
                while (_isRunning)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    using var colorFrame = frameSet.GetColorFrame();
                    using var depthFrame = frameSet.GetDepthFrame();

                    if (colorFrame != null)
                    {
                        byte[] data = new byte[colorFrame.GetDataSize()];
                        colorFrame.CopyData(ref data);
                        renderer.UpdateVideoFrame(colorTextureIndex, (int)colorFrame.GetWidth(),
                            (int)colorFrame.GetHeight(), colorFrame.GetFormat(), data);
                    }

                    if (depthFrame != null)
                    {
                        byte[] data = new byte[depthFrame.GetDataSize()];
                        depthFrame.CopyData(ref data);
                        renderer.UpdateVideoFrame(depthTextureIndex, (int)depthFrame.GetWidth(),
                            (int)depthFrame.GetHeight(), depthFrame.GetFormat(), data);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Start stream error: {ex.Message}");
            }
        }
    }
}