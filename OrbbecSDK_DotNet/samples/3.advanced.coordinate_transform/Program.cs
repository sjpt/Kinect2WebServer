using Orbbec;

namespace Samples.CoordinateTransform
{
    class Program
    {
        private static bool _shouldExit = false;

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("Coordinate Transform - Starting...");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _shouldExit = true;
            };

            Pipeline? pipe = null;
            try
            {
                pipe = new Pipeline();
                using var config = new Config();

                config.EnableVideoStream(StreamType.OB_STREAM_DEPTH);
                config.EnableStream(SensorType.OB_SENSOR_COLOR);

                config.SetFrameAggregateOutputMode(FrameAggregateOutputMode.OB_FRAME_AGGREGATE_OUTPUT_ALL_TYPE_FRAME_REQUIRE);

                pipe.Start(config);

                string? testType = "1";
                while (!_shouldExit)
                {
                    PrintUsage();
                    testType = InputWatcher();

                    using var frameSet = pipe.WaitForFrames(100);
                    if (frameSet == null)
                        continue;

                    using var colorFrame = frameSet.GetColorFrame();
                    using var depthFrame = frameSet.GetDepthFrame();

                    if (colorFrame == null || depthFrame == null)
                        continue;

                    if (testType == "1")
                        Transformation2dto2d(colorFrame, depthFrame);
                    else if (testType == "2")
                        Transformation2dto3d(colorFrame, depthFrame);
                    else if (testType == "3")
                        Transformation3dto3d(colorFrame, depthFrame);
                    else if (testType == "4")
                        Transformation3dto2d(colorFrame, depthFrame);
                    else
                        Console.WriteLine("Invalid command");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
            finally
            {
                pipe?.Stop();
                Console.WriteLine("CoordinateTransform sample exited.");
            }
        }

        // test the transformation from one 2D coordinate system to another
        private static void Transformation2dto2d(ColorFrame colorFrame, DepthFrame depthFrame)
        {
            // Get the width and height of the color and depth frames
            uint colorFrameWidth = colorFrame.GetWidth();
            uint depthFrameWidth = depthFrame.GetWidth();
            uint colorFrameHeight = colorFrame.GetHeight();
            uint depthFrameHeight = depthFrame.GetHeight();

            // Get the stream profiles for the color and depth frames
            var colorProfile = colorFrame.GetStreamProfile();
            var depthProfile = depthFrame.GetStreamProfile();
            var extrinsicD2C = depthProfile.GetExtrinsicTo(colorProfile);

            // Get the intrinsic and distortion parameters for the color and depth streams
            var colorVideoStreamProfile = colorProfile.As<VideoStreamProfile>();
            var colorIntrinsic = colorVideoStreamProfile.GetIntrinsic();
            var colorDistortion = colorVideoStreamProfile.GetDistortion();
            var depthVideoStreamProfile = depthProfile.As<VideoStreamProfile>();
            var depthIntrinsic = depthVideoStreamProfile.GetIntrinsic();
            var depthDistortion = depthVideoStreamProfile.GetDistortion();
            // Access the depth data from the frame
            byte[] pDepthData = new byte[depthFrame.GetDataSize()];
            depthFrame.CopyData(ref pDepthData);
            uint convertAreaWidth = 3;
            uint convertAreaHeight = 3;

            // Transform depth values to the color frame's coordinate system
            for (uint i = depthFrameHeight / 2; i < (depthFrameHeight / 2 + convertAreaHeight); i++)
            {
                for (uint j = depthFrameWidth / 2; j < (depthFrameWidth / 2 + convertAreaWidth); j++)
                {
                    var sourcePixel = new Point2f { x = j, y = i };
                    var targetPixel = new Point2f();
                    float depthValue = pDepthData[i * depthFrameWidth + j];
                    if (depthValue == 0)
                    {
                        Console.WriteLine("The depth value is 0, so it's recommended to point the camera at a flat surface");
                        continue;
                    }

                    // Demonstrate Depth 2D converted to Color 2D
                    bool result = CoordinateTransformHelper.Transformation2dto2d(sourcePixel, depthValue, depthIntrinsic, depthDistortion, colorIntrinsic,
                                                                                      colorDistortion, extrinsicD2C, ref targetPixel);

                    // Check transformation result and whether the target pixel is within the color frame
                    if (!result || targetPixel.y < 0 || targetPixel.x < 0 || targetPixel.y >= colorFrameHeight || targetPixel.x >= colorFrameWidth)
                    {
                        continue;
                    }

                    // Calculate the index position of the target pixel in the transformation data buffer
                    uint index = (uint)(targetPixel.y * colorFrameWidth + targetPixel.x);
                    if (index >= colorFrameWidth * colorFrameHeight)
                    {
                        continue;
                    }

                    PrintRuslt("depth to color: depth image coordinate transform to color image coordinate", sourcePixel, targetPixel);
                }
            }
        }

        // test the transformation from 2D to 3D coordinates
        private static void Transformation2dto3d(ColorFrame colorFrame, DepthFrame depthFrame)
        {
            // Get the width and height of the color and depth frames
            uint depthFrameWidth = depthFrame.GetWidth();
            uint depthFrameHeight = depthFrame.GetHeight();

            // Get the stream profiles for the color and depth frames
            var colorProfile = colorFrame.GetStreamProfile();
            var depthProfile = depthFrame.GetStreamProfile();
            var extrinsicD2C = depthProfile.GetExtrinsicTo(colorProfile);

            // Get the intrinsic and distortion parameters for the color and depth streams
            var depthIntrinsic = depthProfile.As<VideoStreamProfile>().GetIntrinsic();
            // Access the depth data from the frame
            byte[] pDepthData = new byte[depthFrame.GetDataSize()];
            depthFrame.CopyData(ref pDepthData);
            uint convertAreaWidth = 3;
            uint convertAreaHeight = 3;

            // Transform depth values to the color frame's coordinate system
            for (uint i = depthFrameHeight / 2; i < (depthFrameHeight / 2 + convertAreaHeight); i++)
            {
                for (uint j = depthFrameWidth / 2; j < (depthFrameWidth / 2 + convertAreaWidth); j++)
                {
                    // Get the coordinates of the current pixel
                    var sourcePixel = new Point2f { x = j, y = i };
                    var targetPixel = new Point3f();
                    // Get the depth value of the current pixel
                    float depthValue = pDepthData[i * depthFrameWidth + j];
                    if (depthValue == 0)
                    {
                        Console.WriteLine("The depth value is 0, so it's recommended to point the camera at a flat surface");
                        continue;
                    }

                    // Perform the 2D to 3D transformation
                    bool result = CoordinateTransformHelper.Transformation2dto3d(sourcePixel, depthValue, depthIntrinsic, extrinsicD2C, ref targetPixel);
                    if (!result)
                    {
                        continue;
                    }

                    PrintRuslt("2d to 3D: pixel coordinates and depth transform to point in 3D space", sourcePixel, targetPixel, depthValue);
                }
            }
        }

        // test the transformation from 3D coordinates to 3D coordinates
        private static void Transformation3dto3d(ColorFrame colorFrame, DepthFrame depthFrame)
        {
            // Get the width and height of the color and depth frames
            uint depthFrameWidth = depthFrame.GetWidth();
            uint depthFrameHeight = depthFrame.GetHeight();

            // Get the stream profiles for the color and depth frames
            var colorProfile = colorFrame.GetStreamProfile();
            var depthProfile = depthFrame.GetStreamProfile();
            var extrinsicC2D = colorProfile.GetExtrinsicTo(depthProfile);
            var extrinsicD2C = depthProfile.GetExtrinsicTo(colorProfile);

            // Get the intrinsic and distortion parameters for the color and depth streams
            var depthIntrinsic = depthProfile.As<VideoStreamProfile>().GetIntrinsic();
            // Access the depth data from the frame
            byte[] pDepthData = new byte[depthFrame.GetDataSize()];
            depthFrame.CopyData(ref pDepthData);
            uint convertAreaWidth = 3;
            uint convertAreaHeight = 3;

            // Transform depth values to the color frame's coordinate system
            for (uint i = depthFrameHeight / 2; i < (depthFrameHeight / 2 + convertAreaHeight); i++)
            {
                for (uint j = depthFrameWidth / 2; j < (depthFrameWidth / 2 + convertAreaWidth); j++)
                {
                    // Get the coordinates of the current pixel
                    var sourcePixel = new Point2f { x = j, y = i };
                    var tmpTargetPixel = new Point3f();
                    var targetPixel = new Point3f();
                    // Get the depth value of the current pixel
                    float depthValue = pDepthData[i * depthFrameWidth + j];
                    if (depthValue == 0)
                    {
                        Console.WriteLine("The depth value is 0, so it's recommended to point the camera at a flat surface");
                        continue;
                    }

                    // Perform the 2D to 3D transformation
                    bool result = CoordinateTransformHelper.Transformation2dto3d(sourcePixel, depthValue, depthIntrinsic, extrinsicD2C, ref tmpTargetPixel);
                    if (!result)
                    {
                        continue;
                    }
                    PrintRuslt("2d to 3D: pixel coordinates and depth transform to point in 3D space", sourcePixel, tmpTargetPixel, depthValue);

                    // Perform the 3D to 3D transformation
                    result = CoordinateTransformHelper.Transformation3dto3d(tmpTargetPixel, extrinsicC2D, ref targetPixel);
                    if (!result)
                    {
                        continue;
                    }
                    PrintRuslt("3d to 3D: transform 3D coordinates relative to one sensor to 3D coordinates relative to another viewpoint", tmpTargetPixel, targetPixel);
                }
            }
        }

        // test the transformation from 3D coordinates back to 2D coordinates
        private static void Transformation3dto2d(ColorFrame colorFrame, DepthFrame depthFrame)
        {
            // Get the width and height of the color and depth frames
            uint depthFrameWidth = depthFrame.GetWidth();
            uint depthFrameHeight = depthFrame.GetHeight();

            // Get the stream profiles for the color and depth frames
            var colorProfile = colorFrame.GetStreamProfile();
            var depthProfile = depthFrame.GetStreamProfile();
            var extrinsicC2D = colorProfile.GetExtrinsicTo(depthProfile);
            var extrinsicD2C = depthProfile.GetExtrinsicTo(colorProfile);

            // Get the intrinsic and distortion parameters for the color and depth streams
            var depthVideoStreamProfile = depthProfile.As<VideoStreamProfile>();
            var depthIntrinsic = depthVideoStreamProfile.GetIntrinsic();
            var depthDistortion = depthVideoStreamProfile.GetDistortion();
            // Access the depth data from the frame
            byte[] pDepthData = new byte[depthFrame.GetDataSize()];
            depthFrame.CopyData(ref pDepthData);
            uint convertAreaWidth = 3;
            uint convertAreaHeight = 3;

            // Transform depth values to the color frame's coordinate system
            for (uint i = depthFrameHeight / 2; i < (depthFrameHeight / 2 + convertAreaHeight); i++)
            {
                for (uint j = depthFrameWidth / 2; j < (depthFrameWidth / 2 + convertAreaWidth); j++)
                {
                    // Get the coordinates of the current pixel
                    var sourcePixel = new Point2f { x = (float)j, y = (float)i };
                    var tmpTargetPixel = new Point3f();
                    var targetPixel = new Point2f();
                    // Get the depth value of the current pixel
                    float depthValue = (float)pDepthData[i * depthFrameWidth + j];
                    if (depthValue == 0)
                    {
                        Console.WriteLine("The depth value is 0, so it's recommended to point the camera at a flat surface");
                        continue;
                    }

                    // Perform the 2D to 3D transformation
                    bool result = CoordinateTransformHelper.Transformation2dto3d(sourcePixel, depthValue, depthIntrinsic,
                                                    extrinsicD2C, ref tmpTargetPixel);
                    if (!result)
                    {
                        continue;
                    }
                    PrintRuslt("depth 2d to 3D: pixel coordinates and depth transform to point in 3D space", sourcePixel, tmpTargetPixel, depthValue);

                    // Perform the 3D to 2D transformation
                    result = CoordinateTransformHelper.Transformation3dto2d(tmpTargetPixel, depthIntrinsic, depthDistortion, extrinsicC2D, ref targetPixel);
                    if (!result)
                    {
                        continue;
                    }
                    PrintRuslt("3d to depth 2d : point in 3D space transform to the corresponding pixel coordinates in an image", tmpTargetPixel, targetPixel);
                }
            }
        }

        private static void PrintRuslt(string msg, Point2f sourcePixel, Point2f targetPixel)
        {
            Console.WriteLine($"{msg}: ({sourcePixel.x}, {sourcePixel.y}) -> ({targetPixel.x}, {targetPixel.y})");
        }

        private static void PrintRuslt(string msg, Point2f sourcePixel, Point3f targetPixel, float depthValue)
        {
            Console.WriteLine($"{msg}: depth {depthValue} ({sourcePixel.x}, {sourcePixel.y}) -> ({targetPixel.x}, {targetPixel.y}, {targetPixel.z})");
        }

        private static void PrintRuslt(string msg, Point3f sourcePixel, Point2f targetPixel)
        {
            Console.WriteLine($"{msg}: ({sourcePixel.x}, {sourcePixel.y}, {sourcePixel.z}) -> ({targetPixel.x}, {targetPixel.y})");
        }

        private static void PrintRuslt(string msg, Point3f sourcePixel, Point3f targetPixel)
        {
            Console.WriteLine($"{msg}: ({sourcePixel.x}, {sourcePixel.y}, {sourcePixel.z}) -> ({targetPixel.x}, {targetPixel.y}, {targetPixel.z})");
        }

        private static string? InputWatcher()
        {
            while (true)
            {
                Console.Write("\nInput command:  ");
                var cmd = Console.ReadLine();
                if (cmd == "quit" || cmd == "q")
                {
                    _shouldExit = true;
                }
                return cmd;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Support commands:");
            Console.WriteLine("    1 - transformation 2d to 2d");
            Console.WriteLine("    2 - transformation 2d to 3d");
            Console.WriteLine("    3 - transformation 3d to 3d");
            Console.WriteLine("    4 - transformation 3d to 2d");
            Console.WriteLine(new string('-', 33));
            Console.WriteLine("    quit / q- quit application");
        }
    }
}
