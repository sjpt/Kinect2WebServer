using Samples.Common;
using Orbbec;

namespace Samples.Depth
{
    class Program
    {
        private static volatile bool _isRunning = true;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Depth - Starting...");

            using var renderer = new OrbbecRenderer(title: "Depth");

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
                config = new Config();

                // Enable default depth stream
                config.EnableVideoStream(StreamType.OB_STREAM_DEPTH);

                pipe.Start(config);

                int depthTextureIndex = renderer.AddVideoFrame();
                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                _ = Task.Run(() => StartStream(pipe, renderer, depthTextureIndex));

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
                Console.WriteLine("Depth sample exited.");
            }
        }

        private static void StartStream(Pipeline pipeline, OrbbecRenderer renderer, int depthTextureIndex)
        {
            try
            {
                while (_isRunning)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    using var depthFrame = frameSet.GetDepthFrame();
                    if (depthFrame == null) continue;

                    // for Y16 format depth frame, print the distance of the center pixel every 30 frames
                    if (depthFrame.GetIndex() % 30 == 0 && depthFrame.GetFormat() == Format.OB_FORMAT_Y16)
                    {
                        uint width = depthFrame.GetWidth();
                        uint height = depthFrame.GetHeight();
                        float scale = depthFrame.GetValueScale();

                        byte[] depthData = new byte[depthFrame.GetDataSize()];
                        depthFrame.CopyData(ref depthData);

                        // Get center pixel value (16-bit)
                        int centerIndex = (int)(width * height / 2 + width / 2);
                        ushort centerValue = BitConverter.ToUInt16(depthData, centerIndex * 2);

                        // pixel value multiplied by scale is the actual distance value in millimeters
                        float centerDistance = centerValue * scale;

                        // attention: if the distance is 0, it means that the depth camera cannot detect the object (may be out of detection range)
                        Console.WriteLine($"Facing an object at a distance of {centerDistance:F3} mm.");
                    }

                    byte[] data = new byte[depthFrame.GetDataSize()];
                    depthFrame.CopyData(ref data);
                    renderer.UpdateVideoFrame(depthTextureIndex, (int)depthFrame.GetWidth(),
                        (int)depthFrame.GetHeight(), depthFrame.GetFormat(), data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Start stream error: {ex.Message}");
            }
        }
    }
}