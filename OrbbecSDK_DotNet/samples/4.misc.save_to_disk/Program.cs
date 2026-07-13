using Orbbec;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Samples.SaveToDisk
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Save To Disk Sample - Starting...");
            Console.WriteLine("Saving 5 frames...");

            try
            {
                using var pipeline = new Pipeline();
                using var config = new Config();

                config.EnableStream(StreamType.OB_STREAM_COLOR);
                config.EnableStream(StreamType.OB_STREAM_DEPTH);
                config.SetFrameAggregateOutputMode(FrameAggregateOutputMode.OB_FRAME_AGGREGATE_OUTPUT_ALL_TYPE_FRAME_REQUIRE);

                // Create format converter for color frame conversion
                using var formatConverter = new FormatConvertFilter();

                pipeline.Start(config);

                // Drop several frames
                for (int i = 0; i < 15; i++)
                {
                    pipeline.WaitForFrames(100);
                }

                uint frameIndex = 0;

                while (frameIndex < 5)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet == null)
                    {
                        Console.WriteLine("No frames received in 100ms...");
                        continue;
                    }

                    frameIndex++;

                    using var depthFrame = frameSet.GetDepthFrame();
                    using var colorFrame = frameSet.GetColorFrame();

                    if (depthFrame != null)
                    {
                        SaveDepthFrame(depthFrame, frameIndex);
                    }

                    if (colorFrame != null)
                    {
                        // Convert color frame to RGB format
                        Frame? convertedFrame = colorFrame;
                        var colorFormat = colorFrame.GetFormat();

                        if (colorFormat != Format.OB_FORMAT_RGB)
                        {
                            if (colorFormat == Format.OB_FORMAT_MJPG)
                            {
                                formatConverter.SetConvertFormat(ConvertFormat.FORMAT_MJPG_TO_RGB);
                            }
                            else if (colorFormat == Format.OB_FORMAT_UYVY)
                            {
                                // UYVY to RGB conversion
                                formatConverter.SetConvertFormat(ConvertFormat.FORMAT_YUYV_TO_RGB);
                            }
                            else if (colorFormat == Format.OB_FORMAT_YUYV || colorFormat == Format.OB_FORMAT_YUY2)
                            {
                                formatConverter.SetConvertFormat(ConvertFormat.FORMAT_YUYV_TO_RGB);
                            }
                            else
                            {
                                Console.WriteLine($"Color format {colorFormat} is not supported for conversion!");
                                continue;
                            }
                            convertedFrame = formatConverter.Process(colorFrame);
                        }

                        // Convert RGB to BGR for saving
                        if (convertedFrame != null)
                        {
                            formatConverter.SetConvertFormat(ConvertFormat.FORMAT_RGB_TO_BGR);
                            using var bgrFrame = formatConverter.Process(convertedFrame);
                            if (bgrFrame != null)
                            {
                                SaveColorFrame(bgrFrame, frameIndex);
                            }
                        }

                        // Dispose converted frame if it's different from original
                        if (convertedFrame != null && convertedFrame != colorFrame)
                        {
                            convertedFrame.Dispose();
                        }
                    }
                }

                pipeline.Stop();
                Console.WriteLine("Demo completed. Frames saved to current directory.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey(true);
        }

        static void SaveDepthFrame(Frame frame, uint frameIndex)
        {
            try
            {
                var streamProfile = frame.GetStreamProfile();
                var videoProfile = streamProfile.As<VideoStreamProfile>();
                var width = videoProfile.GetWidth();
                var height = videoProfile.GetHeight();
                var timestamp = frame.GetTimeStamp();
                var fileName = $"Depth_{width}x{height}_{frameIndex}_{timestamp}ms.png";

                var dataSize = frame.GetDataSize();
                var data = new byte[dataSize];
                frame.CopyData(ref data);

                // Save as 16-bit grayscale PNG
                using var image = new Image<L16>((int)width, (int)height);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * (int)width + x;
                        ushort value = (ushort)(data[idx * 2] | (data[idx * 2 + 1] << 8));
                        image[x, y] = new L16(value);
                    }
                }

                image.SaveAsPng(fileName);
                Console.WriteLine($"Depth saved: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving depth frame: {ex.Message}");
            }
        }

        static void SaveColorFrame(Frame frame, uint frameIndex)
        {
            try
            {
                var streamProfile = frame.GetStreamProfile();
                var videoProfile = streamProfile.As<VideoStreamProfile>();
                var width = videoProfile.GetWidth();
                var height = videoProfile.GetHeight();
                var timestamp = frame.GetTimeStamp();
                var fileName = $"Color_{width}x{height}_{frameIndex}_{timestamp}ms.png";

                var dataSize = frame.GetDataSize();
                var data = new byte[dataSize];
                frame.CopyData(ref data);

                // Save as BGR PNG (converted by FormatConvertFilter)
                using var image = new Image<Rgb24>((int)width, (int)height);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = (y * (int)width + x) * 3;
                        byte b = data[idx];
                        byte g = data[idx + 1];
                        byte r = data[idx + 2];
                        image[x, y] = new Rgb24(r, g, b);
                    }
                }

                image.SaveAsPng(fileName);
                Console.WriteLine($"Color saved: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving color frame: {ex.Message}");
            }
        }
    }
}
