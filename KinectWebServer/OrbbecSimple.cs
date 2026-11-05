// direct from Claude
using Orbbec; // namespace name inferred - check actual using in a real sample if this doesn't compile
class OrbProgram
{
    public static void OrbMain()   // Bolt test, not the entry point: Program.Main is (call it from there to try a Bolt) 
    {
        using var pipeline = new Pipeline();

        // Pick a depth profile - Y16 format, 640x576 (Bolt NFOV-ish), 30fps.
        // Adjust width/height/fps to a mode your Bolt actually supports if this throws.
        var depthProfiles = pipeline.GetStreamProfileList(SensorType.OB_SENSOR_DEPTH);
        var depthProfile = depthProfiles.GetVideoStreamProfile(640, 576, Format.OB_FORMAT_Y16, 30);

        var config = new Config();
        config.EnableStream(depthProfile);

        pipeline.Start(config);

        try
        {
            int captured = 0;
            while (captured < 5)
            {
                using var frameSet = pipeline.WaitForFrames(1000); // ms timeout
                if (frameSet == null) continue; // timed out, try again

                var depthFrame = frameSet.GetDepthFrame();
                if (depthFrame == null) continue;

                using (depthFrame)
                {
                    System.Console.WriteLine(
                        $"Frame {captured}: {depthFrame.GetWidth()}x{depthFrame.GetHeight()}, " +
                        $"timestamp {depthFrame.GetTimeStampUs()}us");
                    captured++;
                }
            }
        }
        finally
        {
            pipeline.Stop();
        }
    }
}
