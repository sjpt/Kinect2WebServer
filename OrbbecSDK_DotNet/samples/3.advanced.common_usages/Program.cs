using Orbbec;
using Samples.Common;
using System.Collections.Concurrent;

namespace Samples.CommonUsages
{
    class Program
    {
        private static readonly HashSet<int> OpenniDeviceList = new()
        {
            0x065e,
            0x065b,
            0x069a,
            0x069e,
            0x06aa,
            0x069f,
            0x06a7,
            0x06a0,
            0x06a6,
            0x069d
        };

        private static bool IsOpenniDeviceSeries(int pid) => OpenniDeviceList.Contains(pid);

        private static Pipeline? _pipeline = null;
        private static Device? _device = null;
        private static volatile bool _isRunning = true;
        private static readonly object _deviceLock = new();

        private static AlignFilter? _alignFilter = null;
        private static bool _alignEnabled = false;

        private static List<(SensorType sensorType, FrameType frameType, int textureIndex)> _irSensors = new();
        private static Dictionary<SensorType, VideoStreamProfile> _profilesMap = new();

        private static Context? _context = null;
        private static bool _irRightMirrorSupport = false;

        private static readonly ConcurrentQueue<Frameset> _frameQueue = new();
        private static Frameset? _currentFrameset = null;
        private static readonly object _frameLock = new();

        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("CommonUsages - Starting...\n");

            using var renderer = new OrbbecRenderer(title: "Common Usages");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _isRunning = false;
                renderer.Close();
            };

            try
            {
                _context = new Context();

                if (!InitializeDevice())
                {
                    Console.WriteLine("Failed to initialize device!");
                    return;
                }

                _context.SetDeviceChangedCallback(OnDeviceChanged);

                using var config = CreateConfig();

                _alignFilter = new AlignFilter(StreamType.OB_STREAM_COLOR);

                var colorTextureIndex = renderer.AddVideoFrame();
                var depthTextureIndex = renderer.AddVideoFrame();

                SetupIRSensors(renderer);

                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                _pipeline!.Start(config, OnFrameset);
                Console.WriteLine("Stream started!");

                _ = Task.Run(() => CommandLoop(renderer));
                PrintUsage();

                _ = Task.Run(() => RenderThread(renderer, colorTextureIndex, depthTextureIndex));

                renderer.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                Cleanup();
            }
        }

        static void OnFrameset(Frameset frameset)
        {
            if (!_isRunning)
            {
                frameset.Dispose();
                return;
            }

            lock (_frameLock)
            {
                _currentFrameset?.Dispose();
                _currentFrameset = frameset;
            }
        }

        static void RenderThread(OrbbecRenderer renderer, int colorIndex, int depthIndex)
        {
            while (_isRunning)
            {
                Frameset? frameset = null;
                lock (_frameLock)
                {
                    if (_currentFrameset != null)
                    {
                        frameset = _currentFrameset;
                        _currentFrameset = null;
                    }
                }

                if (frameset != null)
                {
                    try
                    {
                        if (_alignEnabled && _alignFilter != null)
                        {
                            using var alignedFrame = _alignFilter.Process(frameset);
                            if (alignedFrame != null)
                            {
                                using var alignedFrameset = alignedFrame.As<Frameset>();
                                ProcessFrames(alignedFrameset, renderer, colorIndex, depthIndex);
                            }
                            frameset.Dispose();
                        }
                        else
                        {
                            ProcessFrames(frameset, renderer, colorIndex, depthIndex);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Render error: {ex.Message}");
                        frameset.Dispose();
                    }
                }

                Thread.Sleep(10);
            }
        }

        static bool InitializeDevice()
        {
            lock (_deviceLock)
            {
                using var deviceList = _context!.QueryDeviceList();
                if (deviceList.DeviceCount() < 1)
                {
                    Console.WriteLine("No device found!");
                    return false;
                }

                _device = deviceList.GetDevice(0);
                using var deviceInfo = _device.GetDeviceInfo();
                Console.WriteLine($"Found device connected, SN: {deviceInfo.SerialNumber()}");
                Console.WriteLine($"Open device success, SN: {deviceInfo.SerialNumber()}\n");

                _irRightMirrorSupport = _device.IsPropertySupported(PropertyId.OB_PROP_IR_RIGHT_MIRROR_BOOL, PermissionType.OB_PERMISSION_READ_WRITE);

                _pipeline = new Pipeline(_device);

                return true;
            }
        }

        static void Cleanup()
        {
            _pipeline?.Stop();
            _alignFilter?.Dispose();
            _alignFilter = null;

            lock (_frameLock)
            {
                _currentFrameset?.Dispose();
                _currentFrameset = null;
            }

            lock (_deviceLock)
            {
                _device?.Dispose();
                _device = null;
            }

            _context?.Dispose();
            _context = null;
        }

        static void OnDeviceChanged(DeviceList removedList, DeviceList addedList)
        {
            string currentDevSn = "";
            lock (_deviceLock)
            {
                if (_device != null)
                {
                    using var deviceInfo = _device.GetDeviceInfo();
                    currentDevSn = deviceInfo.SerialNumber();
                }
            }

            uint removedCount = removedList.DeviceCount();
            for (uint i = 0; i < removedCount; i++)
            {
                string deviceSN = removedList.SerialNumber(i);
                Console.WriteLine($"Device disconnected, SN: {deviceSN}");
                if (currentDevSn == deviceSN)
                {
                    lock (_deviceLock)
                    {
                        _device?.Dispose();
                        _device = null;
                        _pipeline?.Stop();
                        _pipeline?.Dispose();
                        _pipeline = null;
                    }
                    Console.WriteLine("Current device disconnected");
                }
            }

            uint addedCount = addedList.DeviceCount();
            for (uint i = 0; i < addedCount; i++)
            {
                Console.WriteLine($"Device connected: {addedList.Name(i)}, SN: {addedList.SerialNumber(i)}");
            }
        }

        static Config CreateConfig()
        {
            var config = new Config();
            _profilesMap.Clear();

            var sensorList = _device!.GetSensorList();

            for (uint i = 0; i < sensorList.SensorCount(); i++)
            {
                SensorType sensorType = sensorList.SensorType(i);
                if (sensorType == SensorType.OB_SENSOR_IR ||
                    sensorType == SensorType.OB_SENSOR_IR_LEFT ||
                    sensorType == SensorType.OB_SENSOR_IR_RIGHT ||
                    sensorType == SensorType.OB_SENSOR_COLOR ||
                    sensorType == SensorType.OB_SENSOR_DEPTH)
                {
                    try
                    {
                        using var sensor = sensorList.GetSensor(sensorType);
                        using var profileList = sensor.GetStreamProfileList();
                        if (profileList.ProfileCount() > 0)
                        {
                            using var defProfile = profileList.GetProfile(0);
                            var defVsProfile = defProfile.As<VideoStreamProfile>();
                            _profilesMap[sensorType] = defVsProfile;

                            var sensorName = sensorType switch
                            {
                                SensorType.OB_SENSOR_COLOR => "Color profile: ",
                                SensorType.OB_SENSOR_COLOR_LEFT => "Left Color profile: ",
                                SensorType.OB_SENSOR_COLOR_RIGHT => "Right Color profile: ",
                                SensorType.OB_SENSOR_DEPTH => "Depth profile: ",
                                SensorType.OB_SENSOR_IR => "IR profile: ",
                                SensorType.OB_SENSOR_IR_LEFT => "Left IR profile: ",
                                SensorType.OB_SENSOR_IR_RIGHT => "Right IR profile: ",
                                _ => "unknown profile: "
                            };

                            Console.WriteLine($"{sensorName}{defVsProfile.GetWidth()}x{defVsProfile.GetHeight()} @ {defVsProfile.GetFPS()}fps");

                            config.EnableStream(defVsProfile);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error configuring {sensorType}: {ex.Message}");
                    }
                }
            }

            return config;
        }

        static void SetupIRSensors(OrbbecRenderer renderer)
        {
            _irSensors.Clear();
            var sensorList = _device!.GetSensorList();

            for (uint i = 0; i < sensorList.SensorCount(); i++)
            {
                var sensorType = sensorList.SensorType(i);
                if (sensorType == SensorType.OB_SENSOR_IR ||
                    sensorType == SensorType.OB_SENSOR_IR_LEFT ||
                    sensorType == SensorType.OB_SENSOR_IR_RIGHT)
                {
                    var frameType = sensorType switch
                    {
                        SensorType.OB_SENSOR_IR => FrameType.OB_FRAME_IR,
                        SensorType.OB_SENSOR_IR_LEFT => FrameType.OB_FRAME_IR_LEFT,
                        SensorType.OB_SENSOR_IR_RIGHT => FrameType.OB_FRAME_IR_RIGHT,
                        _ => FrameType.OB_FRAME_IR
                    };
                    var textureIndex = renderer.AddVideoFrame();
                    _irSensors.Add((sensorType, frameType, textureIndex));
                }
            }
        }

        static void ProcessFrames(Frameset frameSet, OrbbecRenderer renderer, int colorIndex, int depthIndex)
        {
            if (frameSet.GetFrameCount() < 1)
            {
                frameSet.Dispose();
                return;
            }

            var colorFrame = frameSet.GetColorFrame();
            if (colorFrame != null)
            {
                using (colorFrame)
                {
                    var format = colorFrame.GetFormat();
                    byte[] data = new byte[colorFrame.GetDataSize()];
                    colorFrame.CopyData(ref data);

                    // Pass colorFrame to support formats requiring Filter conversion
                    renderer.UpdateVideoFrame(colorIndex, (int)colorFrame.GetWidth(),
                        (int)colorFrame.GetHeight(), format, data, colorFrame);
                }
            }

            var depthFrame = frameSet.GetDepthFrame();
            if (depthFrame != null)
            {
                using (depthFrame)
                {
                    byte[] data = new byte[depthFrame.GetDataSize()];
                    depthFrame.CopyData(ref data);
                    renderer.UpdateVideoFrame(depthIndex, (int)depthFrame.GetWidth(),
                        (int)depthFrame.GetHeight(), depthFrame.GetFormat(), data);
                }
            }

            foreach (var irSensor in _irSensors)
            {
                using var irFrame = frameSet.GetFrame(irSensor.frameType);
                if (irFrame != null)
                {
                    using var irVideoFrame = irFrame.As<VideoFrame>();
                    var format = irVideoFrame.GetFormat();
                    byte[] data = new byte[irVideoFrame.GetDataSize()];
                    irVideoFrame.CopyData(ref data);

                    // Pass irFrame to support formats requiring Filter conversion
                    renderer.UpdateVideoFrame(irSensor.textureIndex, (int)irVideoFrame.GetWidth(),
                        (int)irVideoFrame.GetHeight(), format, data, irFrame);
                }
            }

            frameSet.Dispose();
        }

        static bool NeedsFilterConversion(Format format)
        {
            return format == Format.OB_FORMAT_MJPG ||
                   format == Format.OB_FORMAT_YUYV ||
                   format == Format.OB_FORMAT_YUY2 ||
                   format == Format.OB_FORMAT_UYVY ||
                   format == Format.OB_FORMAT_NV12 ||
                   format == Format.OB_FORMAT_NV21 ||
                   format == Format.OB_FORMAT_I420 ||
                   format == Format.OB_FORMAT_BGR ||
                   format == Format.OB_FORMAT_RGBA;
        }

        static void CommandLoop(OrbbecRenderer renderer)
        {
            while (_isRunning)
            {
                try
                {
                    Console.Write("\nInput command:  ");
                    var cmd = Console.ReadLine()?.Trim().ToLower();
                    if (string.IsNullOrEmpty(cmd))
                        continue;

                    if (cmd == "quit" || cmd == "q")
                    {
                        _isRunning = false;
                        renderer.Close();
                        break;
                    }

                    CommandProcess(cmd);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Command error: {ex.Message}");
                }
            }
        }

        static void CommandProcess(string cmd)
        {
            if (cmd == "info" || cmd == "i")
            {
                GetDeviceInformation();
            }
            else if (cmd == "param" || cmd == "p")
            {
                GetCameraParams();
            }
            else if (cmd == "laser" || cmd == "l")
            {
                SwitchLaser();
            }
            else if (cmd == "ldp" || cmd == "d")
            {
                SwitchLDP();
            }
            else if (cmd == "ldp status" || cmd == "ds")
            {
                GetLDPStatus();
            }
            else if (cmd == "color ae" || cmd == "ca")
            {
                SwitchColorAE();
            }
            else if (cmd == "inc color value" || cmd == "cei")
            {
                SetColorExposureValue(true);
            }
            else if (cmd == "dec color value" || cmd == "ced")
            {
                SetColorExposureValue(false);
            }
            else if (cmd == "inc color gain" || cmd == "cgi")
            {
                SetColorGainValue(true);
            }
            else if (cmd == "dec color gain" || cmd == "cgd")
            {
                SetColorGainValue(false);
            }
            else if (cmd == "inc depth value" || cmd == "dei")
            {
                SetDepthExposureValue(true);
            }
            else if (cmd == "dec depth value" || cmd == "ded")
            {
                SetDepthExposureValue(false);
            }
            else if (cmd == "inc depth gain" || cmd == "dgi")
            {
                SetDepthGainValue(true);
            }
            else if (cmd == "dec depth gain" || cmd == "dgd")
            {
                SetDepthGainValue(false);
            }
            else if (cmd == "depth ae" || cmd == "da")
            {
                SwitchDepthAE();
            }
            else if (cmd == "color mirror" || cmd == "cm")
            {
                SwitchColorMirror();
            }
            else if (cmd == "depth mirror" || cmd == "dm")
            {
                SwitchDepthMirror();
            }
            else if (cmd == "ir mirror" || cmd == "im")
            {
                SwitchIRMirror();
            }
            else if (cmd == "ir right mirror" || cmd == "irm")
            {
                SwitchIRRightMirror();
            }
            else if (cmd == "align" || cmd == "a")
            {
                _alignEnabled = !_alignEnabled;
                Console.WriteLine($"D2C Align: {(_alignEnabled ? "ON" : "OFF")}");
            }
            else if (cmd == "help" || cmd == "?")
            {
                PrintUsage();
            }
            else
            {
                Console.WriteLine("Unsupported command received! Input \"help\" to get usage");
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Support commands:");
            Console.WriteLine("    info / i - get device information");
            Console.WriteLine("    param / p - get camera parameter");
            Console.WriteLine("    laser / l - on/off laser");
            Console.WriteLine("    ldp / d - on/off LDP");
            Console.WriteLine("    ldp status / ds - get LDP status");
            Console.WriteLine("    color ae / ca - on/off Color auto exposure");
            Console.WriteLine("    inc color value / cei - increase Color exposure value");
            Console.WriteLine("    dec color value / ced - decrease Color exposure value");
            Console.WriteLine("    inc color gain / cgi - increase Color gain value");
            Console.WriteLine("    dec color gain / cgd - decrease Color gain value");
            Console.WriteLine("    color mirror / cm - on/off color mirror");
            Console.WriteLine("    depth ae / da - on/off Depth/IR auto exposure");
            Console.WriteLine("    depth mirror / dm - on/off Depth mirror");
            Console.WriteLine("    inc depth value / dei - increase Depth/IR exposure value");
            Console.WriteLine("    dec depth value / ded - decrease Depth/IR exposure value");
            Console.WriteLine("    inc depth gain / dgi - increase Depth/IR gain value");
            Console.WriteLine("    dec depth gain / dgd - decrease Depth/IR gain value");
            Console.WriteLine("    ir mirror / im - on/off Ir mirror");
            if (_irRightMirrorSupport)
            {
                Console.WriteLine("    ir right mirror / irm - on/off Ir right mirror");
            }
            Console.WriteLine("    align / a - toggle D2C align");
            Console.WriteLine("--------------------------------");
            Console.WriteLine("    help / ? - print usage");
            Console.WriteLine("    quit / q- quit application");
        }

        static void GetDeviceInformation()
        {
            lock (_deviceLock)
            {
                if (_device == null)
                {
                    Console.WriteLine("Device not available");
                    return;
                }

                try
                {
                    using var deviceInfo = _device.GetDeviceInfo();
                    Console.WriteLine("-Device name: " + deviceInfo.Name());
                    Console.WriteLine($"-Device pid: 0x{deviceInfo.Pid():X4} vid: 0x{deviceInfo.Vid():X4} uid: {deviceInfo.Uid()}");
                    Console.WriteLine("-Firmware version: " + deviceInfo.FirmwareVersion());
                    Console.WriteLine("-Serial number: " + deviceInfo.SerialNumber());
                    Console.WriteLine("-ConnectionType: " + deviceInfo.ConnectionType());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting device info: {ex.Message}");
                }
            }
        }

        static void GetCameraParams()
        {
            lock (_deviceLock)
            {
                if (_device == null)
                {
                    Console.WriteLine("Device not available");
                    return;
                }

                if (_pipeline == null)
                {
                    Console.WriteLine("Pipeline not available");
                    return;
                }

                try
                {
                    foreach (var (sensorType, profile) in _profilesMap)
                    {
                        var intrinsics = profile.GetIntrinsic();
                        var distortion = profile.GetDistortion();
                        var typeString = sensorType.ToString().Replace("OB_SENSOR_", "");

                        Console.WriteLine($"{typeString} intrinsics: fx:{intrinsics.fx}, fy: {intrinsics.fy}, cx: {intrinsics.cx}, cy: {intrinsics.cy}, width: {intrinsics.width}, height: {intrinsics.height}");
                        Console.WriteLine($"{typeString} distortion: k1:{distortion.k1}, k2:{distortion.k2}, k3:{distortion.k3}, k4:{distortion.k4}, k5:{distortion.k5}, k6:{distortion.k6}, p1:{distortion.p1}, p2:{distortion.p2}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void SwitchLaser()
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    PropertyId propertyId;
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_LASER_BOOL, PermissionType.OB_PERMISSION_READ))
                    {
                        propertyId = PropertyId.OB_PROP_LASER_BOOL;
                    }
                    else if (_device.IsPropertySupported(PropertyId.OB_PROP_LASER_CONTROL_INT, PermissionType.OB_PERMISSION_READ))
                    {
                        propertyId = PropertyId.OB_PROP_LASER_CONTROL_INT;
                    }
                    else
                    {
                        Console.WriteLine("Laser switch property is not supported.");
                        return;
                    }

                    bool currentValue = _device.GetBoolProperty(propertyId);
                    if (_device.IsPropertySupported(propertyId, PermissionType.OB_PERMISSION_WRITE))
                    {
                        _device.SetBoolProperty(propertyId, !currentValue);
                        if (!currentValue)
                        {
                            Console.WriteLine("laser turn on!");
                        }
                        else
                        {
                            Console.WriteLine("laser turn off!");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Laser switch property is not supported.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error switching laser: {ex.Message}");
                }
            }
        }

        static void SwitchLDP()
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_LDP_BOOL, PermissionType.OB_PERMISSION_READ))
                    {
                        bool currentValue = _device.GetBoolProperty(PropertyId.OB_PROP_LDP_BOOL);
                        if (_device.IsPropertySupported(PropertyId.OB_PROP_LDP_BOOL, PermissionType.OB_PERMISSION_WRITE))
                        {
                            _device.SetBoolProperty(PropertyId.OB_PROP_LDP_BOOL, !currentValue);
                            if (!currentValue)
                            {
                                Console.WriteLine("LDP turn on!");
                            }
                            else
                            {
                                Console.WriteLine("LDP turn off!");
                            }
                            Console.WriteLine("Attention: For some models, it is require to restart depth stream after turn on/of LDP.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("LDP switch property is not supported.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error switching LDP: {ex.Message}");
                }
            }
        }

        static void GetLDPStatus()
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_LDP_STATUS_BOOL, PermissionType.OB_PERMISSION_READ))
                    {
                        bool status = _device.GetBoolProperty(PropertyId.OB_PROP_LDP_STATUS_BOOL);
                        Console.WriteLine($"LDP status:{status}");
                    }
                    else
                    {
                        Console.WriteLine("LDP status property is not supported.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting LDP status: {ex.Message}");
                }
            }
        }

        static void SwitchColorAE()
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_COLOR_AUTO_EXPOSURE_BOOL, PermissionType.OB_PERMISSION_READ))
                    {
                        bool currentValue = _device.GetBoolProperty(PropertyId.OB_PROP_COLOR_AUTO_EXPOSURE_BOOL);
                        if (_device.IsPropertySupported(PropertyId.OB_PROP_COLOR_AUTO_EXPOSURE_BOOL, PermissionType.OB_PERMISSION_WRITE))
                        {
                            _device.SetBoolProperty(PropertyId.OB_PROP_COLOR_AUTO_EXPOSURE_BOOL, !currentValue);
                            if (!currentValue)
                            {
                                Console.WriteLine("Color Auto-Exposure on!");
                            }
                            else
                            {
                                Console.WriteLine("Color Auto-Exposure off!");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Color auto exposure not supported");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void SwitchDepthAE()
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_DEPTH_AUTO_EXPOSURE_BOOL, PermissionType.OB_PERMISSION_READ))
                    {
                        bool currentValue = _device.GetBoolProperty(PropertyId.OB_PROP_DEPTH_AUTO_EXPOSURE_BOOL);
                        if (_device.IsPropertySupported(PropertyId.OB_PROP_DEPTH_AUTO_EXPOSURE_BOOL, PermissionType.OB_PERMISSION_WRITE))
                        {
                            _device.SetBoolProperty(PropertyId.OB_PROP_DEPTH_AUTO_EXPOSURE_BOOL, !currentValue);
                            if (!currentValue)
                            {
                                Console.WriteLine("Depth Auto-Exposure on!");
                            }
                            else
                            {
                                Console.WriteLine("Depth Auto-Exposure off!");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Depth auto exposure not supported");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void SwitchDepthMirror()
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_DEPTH_MIRROR_BOOL, PermissionType.OB_PERMISSION_READ))
                    {
                        bool currentValue = _device.GetBoolProperty(PropertyId.OB_PROP_DEPTH_MIRROR_BOOL);
                        if (_device.IsPropertySupported(PropertyId.OB_PROP_DEPTH_MIRROR_BOOL, PermissionType.OB_PERMISSION_WRITE))
                        {
                            _device.SetBoolProperty(PropertyId.OB_PROP_DEPTH_MIRROR_BOOL, !currentValue);
                            if (!currentValue)
                            {
                                Console.WriteLine("Note: Currently with the D2C(SW) turned on, Depth Mirror will not work!");
                                Console.WriteLine("Depth mirror on!");
                            }
                            else
                            {
                                Console.WriteLine("Depth mirror off!");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Depth mirror switch property is not supported.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Depth mirror not supported");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void SwitchIRMirror()
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_IR_MIRROR_BOOL, PermissionType.OB_PERMISSION_READ))
                    {
                        bool currentValue = _device.GetBoolProperty(PropertyId.OB_PROP_IR_MIRROR_BOOL);
                        if (_device.IsPropertySupported(PropertyId.OB_PROP_IR_MIRROR_BOOL, PermissionType.OB_PERMISSION_WRITE))
                        {
                            _device.SetBoolProperty(PropertyId.OB_PROP_IR_MIRROR_BOOL, !currentValue);
                            if (!currentValue)
                            {
                                Console.WriteLine("IR mirror on!");
                            }
                            else
                            {
                                Console.WriteLine("IR mirror off!");
                            }
                        }
                        else
                        {
                            Console.WriteLine("IR mirror switch property is not supported.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("IR mirror not supported");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void SwitchIRRightMirror()
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_IR_RIGHT_MIRROR_BOOL, PermissionType.OB_PERMISSION_READ))
                    {
                        bool currentValue = _device.GetBoolProperty(PropertyId.OB_PROP_IR_RIGHT_MIRROR_BOOL);
                        if (_device.IsPropertySupported(PropertyId.OB_PROP_IR_RIGHT_MIRROR_BOOL, PermissionType.OB_PERMISSION_WRITE))
                        {
                            _device.SetBoolProperty(PropertyId.OB_PROP_IR_RIGHT_MIRROR_BOOL, !currentValue);
                            if (!currentValue)
                            {
                                Console.WriteLine("IR Right mirror on!");
                            }
                            else
                            {
                                Console.WriteLine("IR Right mirror off!");
                            }
                        }
                        else
                        {
                            Console.WriteLine("IR mirror switch property is not supported.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("IR right mirror not supported");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void SwitchColorMirror()
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_COLOR_MIRROR_BOOL, PermissionType.OB_PERMISSION_READ))
                    {
                        bool currentValue = _device.GetBoolProperty(PropertyId.OB_PROP_COLOR_MIRROR_BOOL);
                        if (_device.IsPropertySupported(PropertyId.OB_PROP_COLOR_MIRROR_BOOL, PermissionType.OB_PERMISSION_WRITE))
                        {
                            _device.SetBoolProperty(PropertyId.OB_PROP_COLOR_MIRROR_BOOL, !currentValue);
                            if (!currentValue)
                            {
                                Console.WriteLine("Color mirror on!");
                            }
                            else
                            {
                                Console.WriteLine("Color mirror off!");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Color mirror switch property is not supported.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Color mirror not supported");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void SetColorExposureValue(bool increase)
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_COLOR_AUTO_EXPOSURE_BOOL, PermissionType.OB_PERMISSION_READ_WRITE))
                    {
                        bool aeEnabled = _device.GetBoolProperty(PropertyId.OB_PROP_COLOR_AUTO_EXPOSURE_BOOL);
                        if (aeEnabled)
                        {
                            _device.SetBoolProperty(PropertyId.OB_PROP_COLOR_AUTO_EXPOSURE_BOOL, false);
                            Console.WriteLine("Color AE close.");
                        }
                    }

                    if (_device.IsPropertySupported(PropertyId.OB_PROP_COLOR_EXPOSURE_INT, PermissionType.OB_PERMISSION_READ))
                    {
                        var range = _device.GetIntPropertyRange(PropertyId.OB_PROP_COLOR_EXPOSURE_INT);
                        Console.WriteLine($"Color current exposure max:{range.max}, min:{range.min}");

                        int currentValue = _device.GetIntProperty(PropertyId.OB_PROP_COLOR_EXPOSURE_INT);
                        Console.WriteLine($"Color current exposure:{currentValue}");

                        if (_device.IsPropertySupported(PropertyId.OB_PROP_COLOR_EXPOSURE_INT, PermissionType.OB_PERMISSION_WRITE))
                        {
                            int value;
                            if (increase)
                            {
                                value = currentValue + (range.max - range.min) / 10;
                                if (value > range.max)
                                    value = range.max;
                            }
                            else
                            {
                                value = currentValue - (range.max - range.min) / 10;
                                if (value < range.min)
                                    value = range.min;
                            }

                            value = range.min + (value - range.min) / range.step * range.step;

                            Console.WriteLine($"Set color exposure:{value}");
                            _device.SetIntProperty(PropertyId.OB_PROP_COLOR_EXPOSURE_INT, value);
                        }
                        else
                        {
                            Console.WriteLine("Color exposure set property is not supported.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Color exposure get property is not supported.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void SetDepthExposureValue(bool increase)
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_DEPTH_AUTO_EXPOSURE_BOOL, PermissionType.OB_PERMISSION_READ_WRITE))
                    {
                        bool aeEnabled = _device.GetBoolProperty(PropertyId.OB_PROP_DEPTH_AUTO_EXPOSURE_BOOL);
                        if (aeEnabled)
                        {
                            _device.SetBoolProperty(PropertyId.OB_PROP_DEPTH_AUTO_EXPOSURE_BOOL, false);
                            Console.WriteLine("Depth AE close.");
                        }
                    }

                    if (_device.IsPropertySupported(PropertyId.OB_PROP_DEPTH_EXPOSURE_INT, PermissionType.OB_PERMISSION_READ))
                    {
                        var range = _device.GetIntPropertyRange(PropertyId.OB_PROP_DEPTH_EXPOSURE_INT);
                        Console.WriteLine($"Depth current exposure max:{range.max}, min:{range.min}");

                        int currentValue = _device.GetIntProperty(PropertyId.OB_PROP_DEPTH_EXPOSURE_INT);
                        Console.WriteLine($"Depth current exposure:{currentValue}");

                        if (_device.IsPropertySupported(PropertyId.OB_PROP_DEPTH_EXPOSURE_INT, PermissionType.OB_PERMISSION_WRITE))
                        {
                            int value;
                            if (increase)
                            {
                                value = currentValue + (range.max - range.min) / 10;
                                if (value > range.max)
                                    value = range.max;
                            }
                            else
                            {
                                value = currentValue - (range.max - range.min) / 10;
                                if (value < range.min)
                                    value = range.min;
                            }

                            value = range.min + (value - range.min) / range.step * range.step;

                            Console.WriteLine($"Set depth exposure:{value}");
                            _device.SetIntProperty(PropertyId.OB_PROP_DEPTH_EXPOSURE_INT, value);
                        }
                        else
                        {
                            Console.WriteLine("Depth exposure set property is not supported.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Depth exposure get property is not supported.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void SetColorGainValue(bool increase)
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_COLOR_GAIN_INT, PermissionType.OB_PERMISSION_READ))
                    {
                        var range = _device.GetIntPropertyRange(PropertyId.OB_PROP_COLOR_GAIN_INT);
                        Console.WriteLine($"Color current gain max:{range.max}, min:{range.min}");

                        int currentValue = _device.GetIntProperty(PropertyId.OB_PROP_COLOR_GAIN_INT);
                        Console.WriteLine($"Color current gain:{currentValue}");

                        if (_device.IsPropertySupported(PropertyId.OB_PROP_COLOR_GAIN_INT, PermissionType.OB_PERMISSION_WRITE))
                        {
                            int value;
                            if (increase)
                            {
                                value = currentValue + (range.max - range.min) / 10;
                                if (value > range.max)
                                    value = range.max;
                            }
                            else
                            {
                                value = currentValue - (range.max - range.min) / 10;
                                if (value < range.min)
                                    value = range.min;
                            }

                            value = range.min + (value - range.min) / range.step * range.step;

                            Console.WriteLine($"Set color gain:{value}");
                            _device.SetIntProperty(PropertyId.OB_PROP_COLOR_GAIN_INT, value);
                        }
                        else
                        {
                            Console.WriteLine("Color gain set property is not supported.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Color gain get property is not supported.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        static void SetDepthGainValue(bool increase)
        {
            lock (_deviceLock)
            {
                if (_device == null) return;

                try
                {
                    if (_device.IsPropertySupported(PropertyId.OB_PROP_DEPTH_GAIN_INT, PermissionType.OB_PERMISSION_READ))
                    {
                        var range = _device.GetIntPropertyRange(PropertyId.OB_PROP_DEPTH_GAIN_INT);
                        Console.WriteLine($"Depth current gain max:{range.max}, min:{range.min}");

                        int currentValue = _device.GetIntProperty(PropertyId.OB_PROP_DEPTH_GAIN_INT);
                        Console.WriteLine($"Depth current gain:{currentValue}");

                        if (_device.IsPropertySupported(PropertyId.OB_PROP_DEPTH_GAIN_INT, PermissionType.OB_PERMISSION_WRITE))
                        {
                            int value;
                            using var deviceInfo = _device.GetDeviceInfo();
                            int vid = (int)deviceInfo.Vid();
                            int pid = int.Parse(deviceInfo.Pid().Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);

                            if (IsOpenniDeviceSeries(pid) && vid == 0x2BC5)
                            {
                                if (increase)
                                {
                                    value = currentValue + 1;
                                    if (value > range.max)
                                        value = range.max;
                                }
                                else
                                {
                                    value = currentValue - 1;
                                    if (value < range.min)
                                        value = range.min;
                                }
                            }
                            else
                            {
                                if (increase)
                                {
                                    value = currentValue + (range.max - range.min) / 10;
                                    if (value > range.max)
                                        value = range.max;
                                }
                                else
                                {
                                    value = currentValue - (range.max - range.min) / 10;
                                    if (value < range.min)
                                        value = range.min;
                                }
                                value = range.min + (value - range.min) / range.step * range.step;
                            }

                            Console.WriteLine($"Set depth gain:{value}");
                            _device.SetIntProperty(PropertyId.OB_PROP_DEPTH_GAIN_INT, value);
                        }
                        else
                        {
                            Console.WriteLine("Depth gain set property is not supported.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Depth gain get property is not supported.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }
    }
}
