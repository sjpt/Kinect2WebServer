using Samples.Common;
using Orbbec;

namespace Samples.Color
{
    class Program
    {
        private static volatile bool _isRunning = true;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Color - Starting...");

            using var renderer = new OrbbecRenderer(title: "Color");

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

                // Use default configuration, automatically select best format (supports MJPG)
                config.EnableStream(SensorType.OB_SENSOR_COLOR);
                pipe.Start(config);

                int colorTextureIndex = renderer.AddVideoFrame();
                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                _ = Task.Run(() => StartStream(pipe, renderer, colorTextureIndex));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                pipe?.Stop();
                Console.WriteLine("Color sample exited.");
            }
        }

        private static void StartStream(Pipeline pipeline, OrbbecRenderer renderer, int colorTextureIndex)
        {
            try
            {
                while (_isRunning)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    using var colorFrame = frameSet.GetColorFrame();

                    if (colorFrame != null)
                    {
                        byte[] data = new byte[colorFrame.GetDataSize()];
                        colorFrame.CopyData(ref data);
                        // Pass the original frame to support formats requiring Filter conversion like MJPG
                        renderer.UpdateVideoFrame(colorTextureIndex, (int)colorFrame.GetWidth(),
                            (int)colorFrame.GetHeight(), colorFrame.GetFormat(), data, colorFrame);
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