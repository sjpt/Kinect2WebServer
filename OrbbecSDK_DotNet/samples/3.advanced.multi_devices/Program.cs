using Orbbec;
using Samples.Common;

namespace Samples.MultiDevices
{
    class Program
    {
        private static volatile bool _isRunning = true;
        private static readonly Dictionary<uint, Pipeline> _pipes = new Dictionary<uint, Pipeline>{};

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Multi Devices - Starting...");

            using var renderer = new OrbbecRenderer(title: "Multi Devices");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                renderer.Close();
            };

            Context? ctx = null;
            try
            {
                ctx = new Context();

                using var deviceList = ctx.QueryDeviceList();
                uint count = deviceList.DeviceCount();
                for (uint i = 0; i < count; ++i)
                {
                    using var device = deviceList.GetDevice(i);
                    var pipe = new Pipeline(device);
                    _pipes.Add(i, pipe);
                }

                foreach (var (i, pipe) in _pipes)
                {
                    using var config = new Config();
                    config.EnableStream(SensorType.OB_SENSOR_COLOR);
                    config.EnableStream(SensorType.OB_SENSOR_DEPTH);
                    pipe.Start(config);
                }

                for (uint i = 0; i < count * 2; ++i)
                {
                    renderer.AddVideoFrame();
                }
                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                _ = Task.Run(() => StartStream(renderer));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                foreach (var (_, pipe) in _pipes)
                {
                    pipe.Stop();
                    pipe.Dispose();
                }
                ctx?.Dispose();
                Console.WriteLine("Multi Devices sample exited.");
            }
        }

        private static void StartStream(OrbbecRenderer renderer)
        {
            try
            {
                while (_isRunning)
                {
                    foreach (var (i, pipe) in _pipes)
                    {
                        using var frameset = pipe.WaitForFrames(100);
                        if (frameset == null)
                            continue;

                        using var colorFrame = frameset.GetColorFrame();
                        using var depthFrame = frameset.GetDepthFrame();

                        int index = (int)i * 2;

                        // Color frame: pass colorFrame to support MJPG decoding
                        if (colorFrame != null)
                        {
                            byte[] colorData = new byte[colorFrame.GetDataSize()];
                            colorFrame.CopyData(ref colorData);
                            renderer.UpdateVideoFrame(index, (int)colorFrame.GetWidth(),
                                (int)colorFrame.GetHeight(), colorFrame.GetFormat(), colorData, colorFrame);
                        }

                        // Depth frame: use normal UpdateVideoFrame
                        if (depthFrame != null)
                        {
                            byte[] depthData = new byte[depthFrame.GetDataSize()];
                            depthFrame.CopyData(ref depthData);
                            renderer.UpdateVideoFrame(index + 1, (int)depthFrame.GetWidth(),
                                (int)depthFrame.GetHeight(), depthFrame.GetFormat(), depthData);
                        }
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