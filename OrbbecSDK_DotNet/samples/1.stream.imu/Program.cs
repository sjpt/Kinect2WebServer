using Orbbec;

class Program
{
    private static bool _isRunning = true;

    static void Main(string[] args)
    {
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            _isRunning = false;
        };

        Console.Clear();
        Console.WriteLine("IMU - Starting...");

        Pipeline? pipe = null;
        try
        {
            pipe = new Pipeline();
            using var device = pipe.GetDevice();

            // Check if device has both accel and gyro sensors
            using var sensorList = device.GetSensorList();
            bool hasAccel = false;
            bool hasGyro = false;
            for (uint i = 0; i < sensorList.SensorCount(); i++)
            {
                var sensorType = sensorList.SensorType(i);
                if (sensorType == SensorType.OB_SENSOR_ACCEL)
                    hasAccel = true;
                if (sensorType == SensorType.OB_SENSOR_GYRO)
                    hasGyro = true;
            }

            if (!hasAccel || !hasGyro)
            {
                Console.WriteLine("Device does not have both Accel and Gyro sensors!");
                Console.WriteLine($"  Accel: {hasAccel}");
                Console.WriteLine($"  Gyro: {hasGyro}");
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine("Device has both Accel and Gyro sensors, starting IMU stream...");

            using var config = new Config();
            config.EnableAccelStream();
            config.EnableGyroStream();
            config.SetFrameAggregateOutputMode(FrameAggregateOutputMode.OB_FRAME_AGGREGATE_OUTPUT_ALL_TYPE_FRAME_REQUIRE);

            pipe.Start(config);

            while (_isRunning)
            {
                using var frameSet = pipe.WaitForFrames(100);
                if (frameSet == null) continue;

                using var accelFrameRaw = frameSet.GetFrame(FrameType.OB_FRAME_ACCEL);
                if (accelFrameRaw != null)
                {
                    using var accelFrame = accelFrameRaw.As<AccelFrame>();
                    var accelIndex = accelFrame.GetIndex();
                    var accelTimeStampUs = accelFrame.GetTimeStampUs();
                    var accelTemperature = accelFrame.GetTemperature();
                    var accelType = accelFrame.GetFrameType();
                    if (accelIndex % 50 == 0)
                    {
                        // print information every  50 frames.
                        var accelValue = accelFrame.GetAccelValue();
                        var obFloat3d = new Float3D { x = accelValue.x, y = accelValue.y, z = accelValue.z };
                        PrintImuValue(obFloat3d, accelIndex, accelTimeStampUs, accelTemperature, accelType, "m/s^2");
                    }
                }

                using var gyroFrameRaw = frameSet.GetFrame(FrameType.OB_FRAME_GYRO);
                if (gyroFrameRaw != null)
                {
                    using var gyroFrame = gyroFrameRaw.As<GyroFrame>();
                    var gyroIndex = gyroFrame.GetIndex();
                    var gyroTimeStampUs = gyroFrame.GetTimeStampUs();
                    var gyroTemperature = gyroFrame.GetTemperature();
                    var gyroType = gyroFrame.GetFrameType();
                    if (gyroIndex % 50 == 0)
                    {
                        // print information every 50 frames.
                        var gyroValue = gyroFrame.GetGyroValue();
                        var obFloat3d = new Float3D { x = gyroValue.x, y = gyroValue.y, z = gyroValue.z };
                        PrintImuValue(obFloat3d, gyroIndex, gyroTimeStampUs, gyroTemperature, gyroType, "rad/s");
                    }
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
            Console.WriteLine("IMU sample exited.");
        }
    }

    private static void PrintImuValue(Float3D obFloat3d, ulong index, ulong timeStampUs, float temperature, FrameType type, string unit)
    {
        Console.WriteLine("frame index: " + index);
        string typeStr = type.ToString();
        Console.WriteLine($"{typeStr} Frame: ");
        Console.WriteLine("{");
        Console.WriteLine($"  tsp = {timeStampUs}");
        Console.WriteLine($"  temperature = {temperature}");
        Console.WriteLine($"  {typeStr}.x = {obFloat3d.x}{unit}");
        Console.WriteLine($"  {typeStr}.y = {obFloat3d.y}{unit}");
        Console.WriteLine($"  {typeStr}.z = {obFloat3d.z}{unit}");
        Console.WriteLine("}");
        Console.WriteLine();
    }
}