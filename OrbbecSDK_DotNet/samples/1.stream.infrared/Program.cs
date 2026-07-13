using Samples.Common;
using Orbbec;

namespace Samples.Infrared
{
    class Program
    {
        private static volatile bool _isRunning = true;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Infrared - Starting...");

            using var renderer = new OrbbecRenderer(title: "Infrared");

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
                var availableIrTypes = GetAvailableIrTypes(device);
                using var config = new Config();

                foreach (var sensorType in availableIrTypes)
                {
                    config.EnableStream(sensorType);
                    Console.WriteLine($"Enabled stream for: {sensorType}");
                }
                pipe.Start(config);

                var textureIndices = new Dictionary<SensorType, int>();
                foreach (var sensorType in availableIrTypes)
                {
                    textureIndices[sensorType] = renderer.AddVideoFrame();
                }
                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                _ = Task.Run(() => StartStream(pipe, renderer, textureIndices));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                pipe?.Stop();
                pipe?.Dispose();
                device?.Dispose();
                Console.WriteLine("Infrared sample exited.");
            }
        }

        private static List<SensorType> GetAvailableIrTypes(Device device)
        {
            var availableTypes = new List<SensorType>();

            try
            {
                using var sensorList = device.GetSensorList();

                for (int i = 0; i < sensorList.SensorCount(); ++i)
                {
                    var sensorType = sensorList.SensorType((uint)i);
                    if (sensorType == SensorType.OB_SENSOR_IR ||
                        sensorType == SensorType.OB_SENSOR_IR_LEFT ||
                        sensorType == SensorType.OB_SENSOR_IR_RIGHT)
                    {
                        availableTypes.Add(sensorType);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get available IR sensor types: {ex.Message}");
            }

            return availableTypes;
        }

        private static void StartStream(Pipeline pipeline, OrbbecRenderer renderer, Dictionary<SensorType, int> textureIndices)
        {
            try
            {
                while (_isRunning)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    foreach (var (sensorType, textureIndex) in textureIndices)
                    {
                        var frameType = sensorType switch
                        {
                            SensorType.OB_SENSOR_IR => FrameType.OB_FRAME_IR,
                            SensorType.OB_SENSOR_IR_LEFT => FrameType.OB_FRAME_IR_LEFT,
                            SensorType.OB_SENSOR_IR_RIGHT => FrameType.OB_FRAME_IR_RIGHT,
                            _ => FrameType.OB_FRAME_IR
                        };

                        using var frame = frameSet.GetFrame(frameType);

                        if (frame != null)
                        {
                            using var irFrame = frame.As<IRFrame>();
                            byte[] data = new byte[irFrame.GetDataSize()];
                            irFrame.CopyData(ref data);
                            renderer.UpdateVideoFrame(textureIndex, (int)irFrame.GetWidth(), (int)irFrame.GetHeight(), irFrame.GetFormat(), data);
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