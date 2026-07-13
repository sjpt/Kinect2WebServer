using Orbbec;
using Samples.Common;

namespace Samples.HDR
{
    class Program
    {
        private static volatile bool _isRunning = true;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("HDR - Starting...");

            using var renderer = new OrbbecRenderer(title: "HDR");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                renderer.Close();
            };

            Pipeline? pipe = null;
            Device? device = null;
            try
            {
                pipe = new Pipeline();
                device = pipe.GetDevice();

                if (!device.IsPropertySupported(PropertyId.OB_STRUCT_DEPTH_HDR_CONFIG, PermissionType.OB_PERMISSION_READ_WRITE))
                {
                    device?.Dispose();
                    device = null;
                    Console.WriteLine("Current default device does not support HDR merge");
                    return;
                }

                using var depthProfile = pipe.GetStreamProfileList(SensorType.OB_SENSOR_DEPTH).GetVideoStreamProfile(0, 0, Format.OB_FORMAT_Y16, 0);
                using var irLeftProfile = pipe.GetStreamProfileList(SensorType.OB_SENSOR_IR_LEFT).GetVideoStreamProfile(0, 0, Format.OB_FORMAT_Y8, 0);
                using var irRightProfile = pipe.GetStreamProfileList(SensorType.OB_SENSOR_IR_RIGHT).GetVideoStreamProfile(0, 0, Format.OB_FORMAT_Y8, 0);

                using var config = new Config();
                config.EnableStream(depthProfile);
                config.EnableStream(irLeftProfile);
                config.EnableStream(irRightProfile);

                using var hdrMerge = FilterFactory.CreateFilter("HDRMerge").As<HdrMerge>();
                var hdrConfig = new HdrConfig
                {
                    enable = 1,
                    exposure_1 = 7500,
                    gain_1 = 24,
                    exposure_2 = 100,
                    gain_2 = 16
                };
                device.SetStructuredData(PropertyId.OB_STRUCT_DEPTH_HDR_CONFIG, hdrConfig);

                pipe.Start(config);

                for (int i = 0; i < 7; ++i)
                {
                    renderer.AddVideoFrame();
                }
                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                _ = Task.Run(() => StartStream(pipe, renderer, hdrMerge));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                var hdrConfig = new HdrConfig { enable = 0 };
                device?.SetStructuredData(PropertyId.OB_STRUCT_DEPTH_HDR_CONFIG, hdrConfig);
                pipe?.Stop();
                device?.Dispose();
                Console.WriteLine("HDR sample exited.");
            }
        }

        private static void StartStream(Pipeline pipeline, OrbbecRenderer renderer, HdrMerge hdrMerge)
        {
            try
            {
                while (_isRunning)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    using var depthFrame = frameSet.GetFrame(FrameType.OB_FRAME_DEPTH)?.As<DepthFrame>();
                    using var irLeftFrame = frameSet.GetFrame(FrameType.OB_FRAME_IR_LEFT)?.As<IRFrame>();
                    using var irRightFrame = frameSet.GetFrame(FrameType.OB_FRAME_IR_RIGHT)?.As<IRFrame>();

                    if (depthFrame == null || irLeftFrame == null || irRightFrame == null)
                        continue;

                    int groupId = (int)depthFrame.GetMetadataValue(FrameMetadataType.OB_FRAME_METADATA_TYPE_HDR_SEQUENCE_INDEX);

                    if (groupId == 0)
                    {
                        UpdateFrame(0, depthFrame, renderer);
                        UpdateFrame(1, irLeftFrame, renderer);
                        UpdateFrame(2, irRightFrame, renderer);
                    }
                    else if (groupId == 1)
                    {
                        UpdateFrame(3, depthFrame, renderer);
                        UpdateFrame(4, irLeftFrame, renderer);
                        UpdateFrame(5, irRightFrame, renderer);
                    }

                    try
                    {
                        using var result = hdrMerge.Process(frameSet);
                        if (result == null) continue;

                        using var resultFrameSet = result.As<Frameset>();
                        using var resultDepthFrame = resultFrameSet.GetFrame(FrameType.OB_FRAME_DEPTH).As<DepthFrame>();

                        UpdateFrame(6, resultDepthFrame, renderer);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("HDRMerge error: " + e.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Start stream error: {ex.Message}");
            }
        }

        private static void UpdateFrame(int index, VideoFrame frame, OrbbecRenderer renderer)
        {
            byte[] data = new byte[frame.GetDataSize()];
            frame.CopyData(ref data);
            renderer.UpdateVideoFrame(index, (int)frame.GetWidth(), (int)frame.GetHeight(), frame.GetFormat(), data);
        }
    }
}