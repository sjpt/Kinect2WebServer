using Orbbec;
using Samples.Common;

namespace LiDAR.Playback
{
    class Program
    {
        static uint frameCount = 0;
        static volatile bool exited = false;
        static PlaybackStatus playStatus = PlaybackStatus.OB_PLAYBACK_STOPPED;
        static readonly object statusLock = new();

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("LiDAR Playback - Starting...");

            try
            {
                // Get Rosbag file path
                Console.WriteLine("Enter the path of the Rosbag file (.bag):");
                Console.Write("Path: ");
                string? filePath = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(filePath) || !filePath.EndsWith(".bag"))
                {
                    Console.WriteLine("Invalid file format. Please provide a .bag file.");
                    return;
                }

                // Create playback device
                var playback = new PlaybackDevice(filePath);
                var pipe = new Pipeline(playback);

                Console.WriteLine($"Duration: {playback.GetDuration()} ms");

                using var config = new Config();

                // Enable all streams
                var sensorList = playback.GetSensorList();
                for (uint i = 0; i < sensorList.SensorCount(); ++i)
                {
                    var sensorType = sensorList.SensorType(i);
                    config.EnableStream(sensorType);
                    Console.WriteLine($"Enabled stream for: {sensorType}");
                }

                config.SetFrameAggregateOutputMode(FrameAggregateOutputMode.OB_FRAME_AGGREGATE_OUTPUT_ANY_SITUATION);

                // Set playback status change callback
                playback.SetPlaybackStatusChangeCallback(OnPlaybackStatusChanged);

                // Start pipeline
                pipe.Start(config, OnFrameSet);

                // Start replay monitor thread
                var replayThread = new Thread(() => ReplayMonitor(pipe, config));
                replayThread.Start();

                Console.WriteLine("\nPlayback started!");
                Console.WriteLine("Press ESC to exit, 'p' or 'P' to pause/resume.");

                // Wait for exit
                while (true)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Escape)
                        {
                            break;
                        }

                        if (key == ConsoleKey.P)
                        {
                            TogglePauseResume(playback);
                        }
                    }
                    Thread.Sleep(10);
                }

                exited = true;
                pipe.Stop();
                replayThread.Join();
                playback.Dispose();

                Console.WriteLine("Exit");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static void OnFrameSet(Frameset frameset)
        {
            if (frameset == null) return;

            try
            {
                if (frameCount % 20 == 0)
                {
                    // Print info for common frame types
                    var frameTypes = new[] { FrameType.OB_FRAME_LIDAR_POINTS, FrameType.OB_FRAME_COLOR, FrameType.OB_FRAME_DEPTH, FrameType.OB_FRAME_ACCEL, FrameType.OB_FRAME_GYRO };

                    foreach (var type in frameTypes)
                    {
                        using var frame = frameset.GetFrame(type);
                        if (frame != null)
                        {
                            Console.WriteLine($"Frame index: {frame.GetIndex()}, tsp: {frame.GetTimeStampUs()}us, format: {frame.GetFormat()}");
                        }
                    }
                }
                frameCount++;
            }
            finally
            {
                frameset.Dispose();
            }
        }

        static void OnPlaybackStatusChanged(PlaybackStatus status)
        {
            lock (statusLock)
            {
                playStatus = status;
                Monitor.Pulse(statusLock);
            }
        }

        static void ReplayMonitor(Pipeline pipe, Config config)
        {
            while (!exited)
            {
                bool shouldReplay = false;

                lock (statusLock)
                {
                    if (playStatus == PlaybackStatus.OB_PLAYBACK_STOPPED)
                    {
                        shouldReplay = true;
                    }
                    else
                    {
                        Monitor.Wait(statusLock, 100);
                    }
                }

                if (exited) break;

                if (shouldReplay)
                {
                    Console.WriteLine("Replay again");
                    pipe.Stop();
                    Thread.Sleep(1000);

                    if (exited) break;

                    lock (statusLock)
                    {
                        playStatus = PlaybackStatus.OB_PLAYBACK_UNKNOWN;
                    }

                    try
                    {
                        pipe.Start(config, OnFrameSet);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Replay failed: {e.Message}");
                    }
                }
            }
            Console.WriteLine("Replay monitor thread exits");
        }

        static void TogglePauseResume(PlaybackDevice playback)
        {
            var status = playback.GetCurrentPlaybackStatus();
            if (status == PlaybackStatus.OB_PLAYBACK_PLAYING)
            {
                playback.Pause();
                Console.WriteLine("Playback paused");
            }
            else if (status == PlaybackStatus.OB_PLAYBACK_PAUSED)
            {
                playback.Resume();
                Console.WriteLine("Playback resumed");
            }
        }
    }
}