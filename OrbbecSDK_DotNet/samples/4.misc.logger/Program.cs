using Orbbec;

namespace Samples.Logger
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Logger Sample - Starting...");

            try
            {
                // Create Pipeline first to ensure SDK is initialized
                using var pipeline = new Pipeline();
                using var config = new Config();

                config.EnableVideoStream(StreamType.OB_STREAM_DEPTH, 0, 0, 0, Format.OB_FORMAT_Y16);
                config.EnableVideoStream(StreamType.OB_STREAM_COLOR, 0, 0, 0, Format.OB_FORMAT_RGB);

                // Then configure logger
                Context.SetLoggerSeverity(LogSeverity.OB_LOG_SEVERITY_INFO);
                Context.SetLoggerToConsole(LogSeverity.OB_LOG_SEVERITY_INFO);

                pipeline.Start(config);

                Console.WriteLine("Stream started. Testing logger...");

                // Wait for some frames
                for (int i = 0; i < 30; i++)
                {
                    using var frameSet = pipeline.WaitForFrames(100);
                    if (frameSet != null && i % 10 == 0)
                    {
                        Console.WriteLine($"Received frame {i}");
                    }
                }

                pipeline.Stop();

                Console.WriteLine("Logger sample completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey(true);
        }
    }
}