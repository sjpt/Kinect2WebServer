using Orbbec;

namespace Samples.Metadata
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Metadata Sample - Starting...");
            Console.WriteLine("Press ESC to exit...");

            try
            {
                using var pipeline = new Pipeline();

                // Create config and enable streams
                using var config = new Config();
                config.EnableStream(StreamType.OB_STREAM_DEPTH);
                config.EnableStream(StreamType.OB_STREAM_COLOR);

                pipeline.Start(config);

                var isRunning = true;
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    isRunning = false;
                };

                while (isRunning)
                {
                    // Check ESC key to exit
                    try
                    {
                        if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                        {
                            isRunning = false;
                            break;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Console input not available, continue without checking
                    }

                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null) continue;

                    var frameCount = frameSet.GetFrameCount();
                    for (uint i = 0; i < frameCount; i++)
                    {
                        using var frame = frameSet.GetFrameByIndex((int)i);
                        var frameIndex = frame.GetIndex();

                        // Print metadata every 30 frames
                        if (frameIndex % 30 == 0)
                        {
                            Console.WriteLine($"\nframe type: {frame.GetFrameType()}");
                            Console.WriteLine($"frame index: {frameIndex}");

                            // Try to read and display all metadata types
                            PrintFrameMetadata(frame);
                        }
                    }
                }

                pipeline.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("Metadata sample exited.");
        }

        static void PrintFrameMetadata(Frame frame)
        {
            // Try to get all metadata types
            var metadataTypes = new[]
            {
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_TIMESTAMP, "OB_FRAME_METADATA_TYPE_TIMESTAMP"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_SENSOR_TIMESTAMP, "OB_FRAME_METADATA_TYPE_SENSOR_TIMESTAMP"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_FRAME_NUMBER, "OB_FRAME_METADATA_TYPE_FRAME_NUMBER"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_AUTO_EXPOSURE, "OB_FRAME_METADATA_TYPE_AUTO_EXPOSURE"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_EXPOSURE, "OB_FRAME_METADATA_TYPE_EXPOSURE"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_GAIN, "OB_FRAME_METADATA_TYPE_GAIN"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_AUTO_WHITE_BALANCE, "OB_FRAME_METADATA_TYPE_AUTO_WHITE_BALANCE"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_WHITE_BALANCE, "OB_FRAME_METADATA_TYPE_WHITE_BALANCE"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_BRIGHTNESS, "OB_FRAME_METADATA_TYPE_BRIGHTNESS"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_CONTRAST, "OB_FRAME_METADATA_TYPE_CONTRAST"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_SATURATION, "OB_FRAME_METADATA_TYPE_SATURATION"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_SHARPNESS, "OB_FRAME_METADATA_TYPE_SHARPNESS"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_BACKLIGHT_COMPENSATION, "OB_FRAME_METADATA_TYPE_BACKLIGHT_COMPENSATION"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_HUE, "OB_FRAME_METADATA_TYPE_HUE"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_GAMMA, "OB_FRAME_METADATA_TYPE_GAMMA"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_POWER_LINE_FREQUENCY, "OB_FRAME_METADATA_TYPE_POWER_LINE_FREQUENCY"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_LOW_LIGHT_COMPENSATION, "OB_FRAME_METADATA_TYPE_LOW_LIGHT_COMPENSATION"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_MANUAL_WHITE_BALANCE, "OB_FRAME_METADATA_TYPE_MANUAL_WHITE_BALANCE"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_ACTUAL_FRAME_RATE, "OB_FRAME_METADATA_TYPE_ACTUAL_FRAME_RATE"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_FRAME_RATE, "OB_FRAME_METADATA_TYPE_FRAME_RATE"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_AE_ROI_LEFT, "OB_FRAME_METADATA_TYPE_AE_ROI_LEFT"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_AE_ROI_TOP, "OB_FRAME_METADATA_TYPE_AE_ROI_TOP"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_AE_ROI_RIGHT, "OB_FRAME_METADATA_TYPE_AE_ROI_RIGHT"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_AE_ROI_BOTTOM, "OB_FRAME_METADATA_TYPE_AE_ROI_BOTTOM"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_EXPOSURE_PRIORITY, "OB_FRAME_METADATA_TYPE_EXPOSURE_PRIORITY"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_HDR_SEQUENCE_NAME, "OB_FRAME_METADATA_TYPE_HDR_SEQUENCE_NAME"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_HDR_SEQUENCE_SIZE, "OB_FRAME_METADATA_TYPE_HDR_SEQUENCE_SIZE"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_HDR_SEQUENCE_INDEX, "OB_FRAME_METADATA_TYPE_HDR_SEQUENCE_INDEX"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_LASER_POWER, "OB_FRAME_METADATA_TYPE_LASER_POWER"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_LASER_POWER_LEVEL, "OB_FRAME_METADATA_TYPE_LASER_POWER_LEVEL"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_LASER_STATUS, "OB_FRAME_METADATA_TYPE_LASER_STATUS"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_GPIO_INPUT_DATA, "OB_FRAME_METADATA_TYPE_GPIO_INPUT_DATA"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_DISPARITY_SEARCH_OFFSET, "OB_FRAME_METADATA_TYPE_DISPARITY_SEARCH_OFFSET"),
                (FrameMetadataType.OB_FRAME_METADATA_TYPE_DISPARITY_SEARCH_RANGE, "OB_FRAME_METADATA_TYPE_DISPARITY_SEARCH_RANGE"),
            };

            foreach (var (metaType, name) in metadataTypes)
            {
                try
                {
                    if (frame.HasMetadata(metaType))
                    {
                        var value = frame.GetMetadataValue(metaType);
                        Console.WriteLine($"  metadata type: {name,-50} value: {value}");
                    }
                }
                catch
                {
                    // Metadata type not available for this frame
                }
            }
        }
    }
}