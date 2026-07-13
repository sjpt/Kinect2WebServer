using Orbbec;
using Samples.Common;

namespace LiDAR.QuickStart
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("LiDAR Quick Start - Starting...");

            try
            {
                // Create a pipeline (default)
                using var pipe = new Pipeline();

                // Get the device from pipeline
                using var device = pipe.GetDevice();

                // Check if it's a LiDAR device
                if (!IsLiDARDevice(device))
                {
                    Console.WriteLine("Invalid device, please connect a LiDAR device!");
                    return;
                }

                // Start the pipeline with default config (pass null to use default)
                pipe.Start(null);

                Console.WriteLine("LiDAR stream is started!");
                Console.WriteLine("Press R or r to create LiDAR PointCloud and save to ply file!");
                Console.WriteLine("Press ESC to exit!");

                while (true)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Escape)
                        {
                            break;
                        }

                        if (key == ConsoleKey.R)
                        {
                            Console.WriteLine("Save LiDAR PointCloud to ply file, this will take some time...");

                            // Wait for frameSet from the pipeline (default timeout 1000ms)
                            using var frameset = pipe.WaitForFrames(1000);
                            if (frameset == null)
                            {
                                Console.WriteLine("No frame data, please try again!");
                                continue;
                            }

                            // Get LiDAR points frame
                            using var frame = frameset.GetFrame(FrameType.OB_FRAME_LIDAR_POINTS);
                            if (frame == null)
                            {
                                Console.WriteLine("No LiDAR frame found!");
                                continue;
                            }

                            // Save point cloud data to ply file (not binary)
                            if (PointCloudHelper.SaveLiDARPointcloudToPly("LiDARPoints.ply", frame, false))
                            {
                                Console.WriteLine("LiDARPoints.ply Saved");
                            }
                            else
                            {
                                Console.WriteLine("Failed to save LiDARPoints.ply");
                            }
                        }
                    }
                    Thread.Sleep(10);
                }

                pipe.Stop();
                Console.WriteLine("LiDAR Quick Start exited.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static bool IsLiDARDevice(Device device)
        {
            using var info = device.GetDeviceInfo();
            string name = info.Name();
            return name.Contains("LiDAR", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("MS600", StringComparison.OrdinalIgnoreCase);
        }
    }
}