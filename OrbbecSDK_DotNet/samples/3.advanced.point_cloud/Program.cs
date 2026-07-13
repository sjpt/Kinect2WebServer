using Orbbec;

namespace Samples.PointCloud
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Point Cloud - Starting...");

            Pipeline? pipe = null;
            try
            {
                pipe = new Pipeline();
                using var config = new Config();
                config.EnableVideoStream(StreamType.OB_STREAM_DEPTH);
                config.EnableStream(SensorType.OB_SENSOR_COLOR);
                config.SetFrameAggregateOutputMode(FrameAggregateOutputMode.OB_FRAME_AGGREGATE_OUTPUT_ALL_TYPE_FRAME_REQUIRE);

                pipe.EnableFrameSync();
                pipe.Start(config);

                using var pointCloud = new PointCloudFilter();
                using var align = new AlignFilter(StreamType.OB_STREAM_COLOR);

                Console.WriteLine("Depth and Color stream are started!");
                Console.WriteLine("Press R or r to create RGBD PointCloud and save to ply file!");
                Console.WriteLine("Press D or d to create Depth PointCloud and save to ply file!");
                Console.WriteLine("Press M or m to create RGBD PointCloud and save to Mesh ply file!");
                Console.WriteLine("Press ESC to exit!");

                while (true)
                {
                    var keyInfo = Console.ReadKey(true);
                    Console.WriteLine();

                    switch (keyInfo.Key)
                    {
                        case ConsoleKey.Escape:
                            return;

                        case ConsoleKey.R:
                            SavePointCloud("RGBPoints.ply", Format.OB_FORMAT_RGB_POINT, false,
                                pipe, pointCloud, align);
                            break;

                        case ConsoleKey.D:
                            SavePointCloud("DepthPoints.ply", Format.OB_FORMAT_POINT, false,
                                pipe, pointCloud, align);
                            break;

                        case ConsoleKey.M:
                            SavePointCloud("ColorMeshPoints.ply", Format.OB_FORMAT_RGB_POINT, true,
                                pipe, pointCloud, align);
                            break;

                        default:
                            Console.WriteLine("Invalid selection. Please press R, D, M, or ESC.");
                            Console.WriteLine("Press any key to continue...");
                            Console.ReadKey(true);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                pipe?.Stop();
                Console.WriteLine("PointCloud sample exited.");
            }
        }

        private static void SavePointCloud(string fileName, Format format, bool generateMesh,
            Pipeline pipeline, PointCloudFilter pointCloud, AlignFilter align)
        {
            string pointType = format == Format.OB_FORMAT_RGB_POINT ? "RGBD" : "Depth";
            string meshInfo = generateMesh ? "(mesh)" : "";

            Console.WriteLine($"Saving {pointType} PointCloud{meshInfo} to {fileName}, this will take some time...");

            try
            {
                using var frameSet = pipeline.WaitForFrames(1000);
                if (frameSet == null)
                {
                    Console.WriteLine("No frame received, please try again!");
                    return;
                }
                using var alignedFrameset = align.Process(frameSet);

                pointCloud.SetCreatePointFormat(format);
                using var frame = pointCloud.Process(alignedFrameset);

                PointCloudHelper.SavePointcloudToPly(fileName, frame, false, generateMesh, 50);

                Console.WriteLine($"{fileName} saved successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving {fileName}: {ex.Message}");
            }
        }
    }
}