// Copyright (c) Orbbec Inc. All Rights Reserved.
// Licensed under the MIT License.

using Orbbec;
using Samples.Common;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Samples.MultiDevicesSync
{
    public class DeviceConfigInfo
    {
        [JsonPropertyName("sn")]
        public string DeviceSN { get; set; } = "";

        [JsonPropertyName("syncConfig")]
        public SyncConfig SyncConfig { get; set; } = new SyncConfig();
    }

    public class SyncConfig
    {
        [JsonPropertyName("syncMode")]
        public string SyncMode { get; set; } = "OB_MULTI_DEVICE_SYNC_MODE_FREE_RUN";

        [JsonPropertyName("depthDelayUs")]
        public int DepthDelayUs { get; set; } = 0;

        [JsonPropertyName("colorDelayUs")]
        public int ColorDelayUs { get; set; } = 0;

        [JsonPropertyName("trigger2ImageDelayUs")]
        public int Trigger2ImageDelayUs { get; set; } = 0;

        [JsonPropertyName("triggerOutEnable")]
        public bool TriggerOutEnable { get; set; } = false;

        [JsonPropertyName("triggerOutDelayUs")]
        public int TriggerOutDelayUs { get; set; } = 0;

        [JsonPropertyName("framesPerTrigger")]
        public int FramesPerTrigger { get; set; } = 1;
    }

    public class MultiDeviceSyncConfigRoot
    {
        [JsonPropertyName("devices")]
        public List<DeviceConfigInfo> Devices { get; set; } = new List<DeviceConfigInfo>();
    }

    public class PipelineHolder : IDisposable
    {
        private bool _startStream = false;
        private Pipeline _pipeline;
        private SensorType _sensorType;
        private FrameType _frameType;
        private string _deviceSN;
        private int _deviceIndex;

        private readonly object _queueLock = new object();
        private const int MaxFrameSize = 16;
        private Queue<Frame> _obFrames = new Queue<Frame>();

        public uint HalfTspGap { get; private set; }

        public PipelineHolder(Pipeline pipeline, SensorType sensorType, string deviceSN, int deviceIndex)
        {
            _pipeline = pipeline;
            _sensorType = sensorType;
            _deviceSN = deviceSN;
            _deviceIndex = deviceIndex;
            _frameType = MapFrameType(sensorType);
        }

        ~PipelineHolder()
        {
            Release();
        }

        public void StartStream()
        {
            Console.WriteLine($"startStream: {_deviceSN} sensorType:{_sensorType}");
            try
            {
                if (_pipeline != null)
                {
                    using var profileList = _pipeline.GetStreamProfileList(_sensorType);
                    using var streamProfileRaw = profileList.GetProfile(0);
                    var streamProfile = streamProfileRaw.As<VideoStreamProfile>();
                    var fps = streamProfile.GetFPS();

                    HalfTspGap = (uint)(500.0f / fps + 0.5);

                    var config = new Config();
                    config.EnableStream(_sensorType);

                    _pipeline.Start(config, (frameset) =>
                    {
                        ProcessFrame(frameset);
                    });
                    _startStream = true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"starting stream failed: {_deviceSN}");
                Console.WriteLine($"function:{e.Message}");
            }
        }

        private void ProcessFrame(Frameset frameSet)
        {
            if (frameSet == null) return;
            if (!_startStream) { frameSet.Dispose(); return; }

            Frame? obFrame = null;
            lock (_queueLock)
            {
                obFrame = frameSet.GetFrame(_frameType);
                if (obFrame != null)
                {
                    if (_obFrames.Count >= MaxFrameSize)
                    {
                        var oldFrame = _obFrames.Dequeue();
                        oldFrame?.Dispose();
                    }
                    _obFrames.Enqueue(obFrame);
                }
            }

            frameSet.Dispose();
        }

        public bool IsFrameReady()
        {
            lock (_queueLock)
            {
                return _obFrames.Count > 0;
            }
        }

        public Frame? FrontFrame()
        {
            lock (_queueLock)
            {
                return _obFrames.Count > 0 ? _obFrames.Peek() : null;
            }
        }

        public void PopFrame()
        {
            lock (_queueLock)
            {
                if (_obFrames.Count > 0)
                {
                    _obFrames.Dequeue()?.Dispose();
                }
            }
        }

        public Frame? GetFrame()
        {
            lock (_queueLock)
            {
                if (_obFrames.Count > 0)
                    return _obFrames.Dequeue();
                return null;
            }
        }

        public void StopStream()
        {
            try
            {
                if (_pipeline != null)
                {
                    Console.WriteLine($"stopStream: {_deviceSN} sensorType:{_sensorType}");
                    _startStream = false;
                    _pipeline.Stop();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"stopping stream failed: {_deviceSN}");
                Console.WriteLine($"function:{e.Message}");
            }
        }

        public void Release()
        {
            lock (_queueLock)
            {
                _startStream = false;
                while (_obFrames.Count > 0)
                {
                    _obFrames.Dequeue()?.Dispose();
                }
            }
        }

        private FrameType MapFrameType(SensorType sensorType) => sensorType switch
        {
            SensorType.OB_SENSOR_COLOR => FrameType.OB_FRAME_COLOR,
            SensorType.OB_SENSOR_DEPTH => FrameType.OB_FRAME_DEPTH,
            _ => FrameType.OB_FRAME_UNKNOWN
        };

        public string GetSerialNumber() => _deviceSN;
        public SensorType GetSensorType() => _sensorType;
        public FrameType GetFrameType() => _frameType;
        public int GetDeviceIndex() => _deviceIndex;

        public void Dispose() => Release();
    }

    public class FramePairingManager : IDisposable
    {
        private bool _destroy = false;
        private List<PipelineHolder> _pipelineHolderList = new List<PipelineHolder>();
        private Dictionary<int, PipelineHolder> _depthPipelineHolderList = new Dictionary<int, PipelineHolder>();
        private Dictionary<int, PipelineHolder> _colorPipelineHolderList = new Dictionary<int, PipelineHolder>();

        public FramePairingManager()
        {
            _destroy = false;
        }

        ~FramePairingManager()
        {
            Release();
        }

        public void SetPipelineHolderList(List<PipelineHolder> pipelineHolderList)
        {
            _pipelineHolderList = pipelineHolderList;
            _depthPipelineHolderList.Clear();
            _colorPipelineHolderList.Clear();

            foreach (var pipelineHolder in pipelineHolderList)
            {
                int deviceIndex = pipelineHolder.GetDeviceIndex();
                if (pipelineHolder.GetSensorType() == SensorType.OB_SENSOR_DEPTH)
                    _depthPipelineHolderList[deviceIndex] = pipelineHolder;
                if (pipelineHolder.GetSensorType() == SensorType.OB_SENSOR_COLOR)
                    _colorPipelineHolderList[deviceIndex] = pipelineHolder;
            }
        }

        private bool PipelineHoldersFrameNotEmpty()
        {
            if (_pipelineHolderList.Count == 0) return false;

            foreach (var holder in _pipelineHolderList)
            {
                if (!holder.IsFrameReady()) return false;
            }
            return true;
        }

        private ulong GetFrameTimestampMsec(Frame frame)
        {
            return frame.GetTimeStampUs() / 1000;
        }

        public List<(int DeviceIndex, Frame? DepthFrame, Frame? ColorFrame)> GetFramePairs()
        {
            var framePairs = new List<(int DeviceIndex, Frame? DepthFrame, Frame? ColorFrame)>();

            if (_pipelineHolderList.Count == 0) return framePairs;

            var start = DateTime.Now;
            while (!PipelineHoldersFrameNotEmpty() && !_destroy)
            {
                Thread.Sleep(1);
                var elapsed = (DateTime.Now - start).TotalMilliseconds;
                if (elapsed > 200) return framePairs;
            }

            if (_destroy) return framePairs;

            bool discardFrame = false;
            var depthFramesMap = new Dictionary<int, Frame>();
            var colorFramesMap = new Dictionary<int, Frame>();
            var pipelineHolderVector = new List<PipelineHolder>();

            SortFrameMap(_pipelineHolderList, pipelineHolderVector);

            if (pipelineHolderVector.Count == 0) return framePairs;

            var refHolder = pipelineHolderVector[0];
            var refFrame = refHolder.FrontFrame();
            if (refFrame == null) return framePairs;

            var refTsp = GetFrameTimestampMsec(refFrame);
            var refHalfTspGap = refHolder.HalfTspGap;

            foreach (var item in pipelineHolderVector)
            {
                var tarFrame = item.FrontFrame();
                if (tarFrame == null)
                {
                    discardFrame = true;
                    break;
                }

                var tarHalfTspGap = item.HalfTspGap;
                int index = item.GetDeviceIndex();
                var frameType = item.GetFrameType();
                uint tspHalfGap = tarHalfTspGap > refHalfTspGap ? tarHalfTspGap : refHalfTspGap;

                var tarTsp = GetFrameTimestampMsec(tarFrame);
                long diffTsp = (long)tarTsp - (long)refTsp;

                if (diffTsp > tspHalfGap)
                {
                    discardFrame = true;
                    refHolder.PopFrame();
                    break;
                }

                refHalfTspGap = tarHalfTspGap;

                if (frameType == FrameType.OB_FRAME_DEPTH)
                {
                    var frame = item.GetFrame();
                    if (frame != null)
                        depthFramesMap[index] = frame;
                }
                if (frameType == FrameType.OB_FRAME_COLOR)
                {
                    var frame = item.GetFrame();
                    if (frame != null)
                        colorFramesMap[index] = frame;
                }
            }

            if (discardFrame)
            {
                foreach (var frame in depthFramesMap.Values) frame?.Dispose();
                foreach (var frame in colorFramesMap.Values) frame?.Dispose();
                return framePairs;
            }

            Console.WriteLine("=================================================");

            int depthHolderSize = _depthPipelineHolderList.Count;
            for (int i = 0; i < depthHolderSize; i++)
            {
                depthFramesMap.TryGetValue(i, out var depthFrame);
                colorFramesMap.TryGetValue(i, out var colorFrame);

                if (depthFrame != null)
                {
                    Console.WriteLine($"Device#{i},  depth(us) , frame timestamp={depthFrame.GetTimeStampUs()},global timestamp = {depthFrame.GetGlobalTimeStampUs()},system timestamp = {depthFrame.GetSystemTimeStamp()}");
                }
                if (colorFrame != null)
                {
                    Console.WriteLine($"Device#{i},  color(us) , frame timestamp={colorFrame.GetTimeStampUs()},global timestamp = {colorFrame.GetGlobalTimeStampUs()},system timestamp = {colorFrame.GetSystemTimeStamp()}");
                }

                if (depthFrame != null || colorFrame != null)
                    framePairs.Add((i, depthFrame, colorFrame));
            }

            return framePairs;
        }

        private void SortFrameMap(List<PipelineHolder> pipelineHolders, List<PipelineHolder> pipelineHolderVector)
        {
            pipelineHolderVector.Clear();
            pipelineHolderVector.AddRange(pipelineHolders);

            pipelineHolderVector.Sort((x, y) =>
            {
                var xFrame = x.FrontFrame();
                var yFrame = y.FrontFrame();
                if (xFrame == null || yFrame == null) return 0;

                var xTsp = GetFrameTimestampMsec(xFrame);
                var yTsp = GetFrameTimestampMsec(yFrame);
                return xTsp.CompareTo(yTsp);
            });
        }

        public void Release() => _destroy = true;
        public void Dispose() => Release();
    }

    class Program
    {
        private const string ConfigFile = "./MultiDeviceSyncConfig.json";

        private static volatile bool _isRunning = true;
        private static List<Device> _streamDevList = new List<Device>();
        private static List<Device> _configDevList = new List<Device>();
        private static List<DeviceConfigInfo> _deviceConfigList = new List<DeviceConfigInfo>();
        private static Context? _context;

        private static List<PipelineHolder> _pipelineHolders = new List<PipelineHolder>();
        private static FramePairingManager? _framePairingManager;
        private static FormatConvertFilter? _mjpegConverter = null;
        private static Dictionary<int, (int DepthIndex, int ColorIndex)> _deviceTextureIndices = new Dictionary<int, (int DepthIndex, int ColorIndex)>();

        static int Main(string[] args)
        {
            int choice;
            int exitValue = 0;

            try
            {
                _context = new Context();

                while (true)
                {
                    Console.WriteLine("\n--------------------------------------------------");
                    Console.WriteLine("Please select options: ");
                    Console.WriteLine("  0 --> config devices sync mode.");
                    Console.WriteLine("  1 --> start stream");
                    Console.WriteLine("--------------------------------------------------");
                    Console.Write("Please select input: ");

                    var input = Console.ReadLine();
                    if (!int.TryParse(input, out choice))
                    {
                        Console.WriteLine("Invalid input. Please enter a number [0~1]");
                        continue;
                    }
                    Console.WriteLine();

                    switch (choice)
                    {
                        case 0:
                            exitValue = ConfigMultiDeviceSync();
                            if (exitValue == 0)
                            {
                                Console.WriteLine("Config MultiDeviceSync Success. \n");
                                exitValue = TestMultiDeviceSync();
                            }
                            break;
                        case 1:
                            Console.WriteLine("\nStart Devices video stream.");
                            exitValue = TestMultiDeviceSync();
                            break;
                        default:
                            break;
                    }

                    if (exitValue == 0) break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
                Console.WriteLine("\nPress any key to exit.");
                Console.ReadKey();
                return 1;
            }
            finally
            {
                Cleanup();
            }

            return exitValue;
        }

        static int ConfigMultiDeviceSync()
        {
            try
            {
                if (!LoadConfigFile())
                {
                    Console.WriteLine("load config failed");
                    return -1;
                }

                if (_deviceConfigList.Count == 0)
                {
                    Console.WriteLine($"DeviceConfigList is empty. please check config file: {ConfigFile}");
                    return -1;
                }

                using var devList = _context!.QueryDeviceList();
                int devCount = (int)devList.DeviceCount();
                for (int i = 0; i < devCount; i++)
                {
                    _configDevList.Add(devList.GetDevice((uint)i));
                }

                if (_configDevList.Count == 0)
                {
                    Console.WriteLine("Device list is empty. please check device connection state");
                    return -1;
                }

                foreach (var config in _deviceConfigList)
                {
                    var device = _configDevList.FirstOrDefault(d =>
                    {
                        using var info = d.GetDeviceInfo();
                        return string.Equals(info.SerialNumber(), config.DeviceSN, StringComparison.OrdinalIgnoreCase);
                    });

                    if (device != null)
                    {
                        var curConfig = device.GetMultiDeviceSyncConfig();
                        curConfig.syncMode = StringToOBSyncMode(config.SyncConfig.SyncMode);
                        curConfig.depthDelayUs = config.SyncConfig.DepthDelayUs;
                        curConfig.colorDelayUs = config.SyncConfig.ColorDelayUs;
                        curConfig.trigger2ImageDelayUs = config.SyncConfig.Trigger2ImageDelayUs;
                        curConfig.triggerOutEnable = config.SyncConfig.TriggerOutEnable;
                        curConfig.triggerOutDelayUs = config.SyncConfig.TriggerOutDelayUs;
                        curConfig.framesPerTrigger = config.SyncConfig.FramesPerTrigger;

                        Console.WriteLine($"-Config Device syncMode:{curConfig.syncMode}, syncModeStr:{config.SyncConfig.SyncMode}");
                        device.SetMultiDeviceSyncConfig(curConfig);
                    }
                    Thread.Sleep(10);
                }
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine("configMultiDeviceSync failed! ");
                Console.WriteLine($"Error: {e.Message}");
                return -1;
            }
        }

        static int TestMultiDeviceSync()
        {
            OrbbecRenderer? renderer = null;

            try
            {
                _streamDevList.Clear();
                _pipelineHolders.Clear();
                _deviceTextureIndices.Clear();

                using var devList = _context!.QueryDeviceList();
                uint devCount = devList.DeviceCount();

                if (devCount == 0)
                {
                    Console.WriteLine("No device found! Please connect at least one device.");
                    Console.ReadKey();
                    return -1;
                }

                Console.WriteLine($"Found {devCount} device(s):");

                var primaryDevices = new List<Device>();
                var secondaryDevices = new List<Device>();

                for (uint i = 0; i < devCount; i++)
                {
                    var device = devList.GetDevice(i);
                    var deviceInfo = device.GetDeviceInfo();

                    string deviceName = deviceInfo.Name();
                    string serialNumber = deviceInfo.SerialNumber();

                    Console.WriteLine($"  [{i}] {deviceName} - SN: {serialNumber}");

                    var syncConfig = device.GetMultiDeviceSyncConfig();
                    Console.WriteLine($"      Current Sync Mode: {syncConfig.syncMode}");

                    if (syncConfig.syncMode == MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_PRIMARY)
                    {
                        primaryDevices.Add(device);
                    }
                    else
                    {
                        secondaryDevices.Add(device);
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"Primary devices: {primaryDevices.Count}, Secondary devices: {secondaryDevices.Count}");

                renderer = new OrbbecRenderer(
                    width: 1600,
                    height: 900,
                    title: "MultiDeviceSyncViewer");

                // Enable timestamp display
                renderer.ShowTimestamp = true;

                // Set key prompt with larger, bold, white text, centered at top
                renderer.SetTextDisplay("keyPrompt", "[S] Sync Time    [T] Software Trigger", 0, 15);
                renderer.SetTextFont("keyPrompt", "Arial", 28f);
                renderer.SetTextBold("keyPrompt", true);
                renderer.SetTextColor("keyPrompt", SixLabors.ImageSharp.Color.White);
                renderer.SetTextCentered("keyPrompt", true);

                Console.WriteLine("Secondary devices start...");
                int deviceIndex = 0;
                foreach (var device in secondaryDevices)
                {
                    StartDeviceStreams(device, deviceIndex, renderer);
                    deviceIndex++;
                }

                if (secondaryDevices.Count > 0 && primaryDevices.Count > 0)
                {
                    Console.WriteLine("Waiting 5 seconds for secondary devices to initialize...");
                    Thread.Sleep(5000);
                }

                if (primaryDevices.Count == 0)
                {
                    Console.WriteLine("WARNING primary_devices is empty!!!");
                }
                else
                {
                    Console.WriteLine("Primary device start...");
                    foreach (var device in primaryDevices)
                    {
                        StartDeviceStreams(device, deviceIndex, renderer);
                        deviceIndex++;
                    }
                }

                _context.EnableDeviceClockSync(60000);

                _framePairingManager = new FramePairingManager();
                _framePairingManager.SetPipelineHolderList(_pipelineHolders);

                _mjpegConverter = new FormatConvertFilter();
                _mjpegConverter.SetConvertFormat(ConvertFormat.FORMAT_MJPG_TO_RGB);

                Console.WriteLine("\nMulti-device sync started. Press 'S' to sync time, 'T' to trigger, 'ESC' to quit.");

                renderer.Closing += (e) =>
                {
                    Console.WriteLine("Window closing, stopping...");
                    _isRunning = false;
                };

                renderer.KeyPressed += (key) => HandleKeyPress(key, renderer);

                Task.Run(() => RenderLoop(renderer));
                renderer.Run();

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return -1;
            }
            finally
            {
                _isRunning = false;
                Cleanup();
            }
        }

        static void StartDeviceStreams(Device device, int deviceIndex, OrbbecRenderer renderer)
        {
            var sensorTypes = new[] { SensorType.OB_SENSOR_DEPTH, SensorType.OB_SENSOR_COLOR };
            int depthIndex = -1;
            int colorIndex = -1;

            using var deviceInfo = device.GetDeviceInfo();
            var serialNumber = deviceInfo.SerialNumber();

            foreach (var sensorType in sensorTypes)
            {
                var sensorList = device.GetSensorList();
                bool hasSensor = false;
                for (uint i = 0; i < sensorList.SensorCount(); i++)
                {
                    if (sensorList.SensorType(i) == sensorType)
                    {
                        hasSensor = true;
                        break;
                    }
                }

                if (!hasSensor) continue;

                var pipeline = new Pipeline(device);
                var holder = new PipelineHolder(pipeline, sensorType, serialNumber, deviceIndex);
                _pipelineHolders.Add(holder);
                holder.StartStream();

                int textureIndex = renderer.AddVideoFrame();
                if (sensorType == SensorType.OB_SENSOR_DEPTH)
                    depthIndex = textureIndex;
                else
                    colorIndex = textureIndex;
            }

            if (depthIndex >= 0 || colorIndex >= 0)
            {
                _deviceTextureIndices[deviceIndex] = (depthIndex, colorIndex);
                Console.WriteLine($"  Device [{deviceIndex}] textures: Color={colorIndex}, Depth={depthIndex}");
            }

            _streamDevList.Add(device);
        }

        static void RenderLoop(OrbbecRenderer renderer)
        {
            try
            {
                while (_isRunning)
                {
                    var framePairs = _framePairingManager?.GetFramePairs();

                    if (framePairs != null && framePairs.Count > 0)
                    {
                        foreach (var pair in framePairs)
                        {
                            RenderFramePair(pair, renderer);
                        }
                    }

                    Thread.Sleep(5);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Render loop error: {ex.Message}");
            }
        }

        static void RenderFramePair((int DeviceIndex, Frame? DepthFrame, Frame? ColorFrame) pair, OrbbecRenderer renderer)
        {
            if (!_deviceTextureIndices.TryGetValue(pair.DeviceIndex, out var indices))
            {
                pair.DepthFrame?.Dispose();
                pair.ColorFrame?.Dispose();
                return;
            }

            if (pair.DepthFrame != null && indices.DepthIndex >= 0)
            {
                try
                {
                    var format = pair.DepthFrame.GetFormat();
                    byte[] data = new byte[pair.DepthFrame.GetDataSize()];
                    pair.DepthFrame.CopyData(ref data);

                    int width = 0, height = 0;
                    ulong frameTimestampUs = 0;
                    ulong systemTimestampUs = 0;
                    ulong globalTimestampUs = 0;

                    // Get all frame data while the reference is valid (inside using block)
                    using (var depthFrame = pair.DepthFrame.As<DepthFrame>())
                    {
                        if (depthFrame != null)
                        {
                            width = (int)depthFrame.GetWidth();
                            height = (int)depthFrame.GetHeight();
                        }
                        // Critical: Get timestamps while the reference is still valid
                        // On Linux x64, the underlying handle may be released immediately
                        // after exiting the using block, causing invalid memory access
                        frameTimestampUs = pair.DepthFrame.GetTimeStampUs();
                        systemTimestampUs = pair.DepthFrame.GetSystemTimeStamp();
                        globalTimestampUs = pair.DepthFrame.GetGlobalTimeStampUs();
                    }

                    // Update timestamp information for this texture using the saved values
                    var timestampInfo = new FrameTimestampInfo
                    {
                        DeviceIndex = pair.DeviceIndex,
                        FrameType = "Depth",
                        FrameTimestampUs = frameTimestampUs,
                        SystemTimestampUs = systemTimestampUs,
                        GlobalTimestampUs = globalTimestampUs
                    };
                    renderer.UpdateTimestampInfo(indices.DepthIndex, timestampInfo);

                    renderer.UpdateVideoFrame(indices.DepthIndex, width, height, format, data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Dev{pair.DeviceIndex}] Render depth error: {ex.Message}");
                }
                finally
                {
                    pair.DepthFrame.Dispose();
                }
            }

            if (pair.ColorFrame != null && indices.ColorIndex >= 0)
            {
                try
                {
                    var format = pair.ColorFrame.GetFormat();

                    if (format == Format.OB_FORMAT_MJPG && _mjpegConverter != null)
                    {
                        using var convertedFrame = _mjpegConverter.Process(pair.ColorFrame);
                        if (convertedFrame != null)
                        {
                            try
                            {
                                using var streamProfile = convertedFrame.GetStreamProfile();
                                using var videoProfile = streamProfile.As<VideoStreamProfile>();
                                byte[] data = new byte[convertedFrame.GetDataSize()];
                                convertedFrame.CopyData(ref data);

                                int width = (int)videoProfile.GetWidth();
                                int height = (int)videoProfile.GetHeight();

                                // Update timestamp information for this texture
                                var timestampInfo = new FrameTimestampInfo
                                {
                                    DeviceIndex = pair.DeviceIndex,
                                    FrameType = "Color",
                                    FrameTimestampUs = pair.ColorFrame.GetTimeStampUs(),
                                    SystemTimestampUs = pair.ColorFrame.GetSystemTimeStamp(),
                                    GlobalTimestampUs = pair.ColorFrame.GetGlobalTimeStampUs()
                                };
                                renderer.UpdateTimestampInfo(indices.ColorIndex, timestampInfo);

                                renderer.UpdateVideoFrame(indices.ColorIndex, width, height, Format.OB_FORMAT_RGB, data);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Dev{pair.DeviceIndex}] MJPG render failed: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        byte[] data = new byte[pair.ColorFrame.GetDataSize()];
                        pair.ColorFrame.CopyData(ref data);

                        int width = 0, height = 0;
                        using (var profile = pair.ColorFrame.GetStreamProfile())
                        {
                            using (var videoProfile = profile.As<VideoStreamProfile>())
                            {
                                if (videoProfile != null)
                                {
                                    width = (int)videoProfile.GetWidth();
                                    height = (int)videoProfile.GetHeight();
                                }
                            }
                        }

                        // Update timestamp information for this texture
                        var timestampInfo = new FrameTimestampInfo
                        {
                            DeviceIndex = pair.DeviceIndex,
                            FrameType = "Color",
                            FrameTimestampUs = pair.ColorFrame.GetTimeStampUs(),
                            SystemTimestampUs = pair.ColorFrame.GetSystemTimeStamp(),
                            GlobalTimestampUs = pair.ColorFrame.GetGlobalTimeStampUs()
                        };
                        renderer.UpdateTimestampInfo(indices.ColorIndex, timestampInfo);

                        renderer.UpdateVideoFrame(indices.ColorIndex, width, height, format, data);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Render color error: {ex.Message}");
                }
                finally
                {
                    pair.ColorFrame.Dispose();
                }
            }
        }

        static void HandleKeyPress(Keys key, OrbbecRenderer win)
        {
            if (key == Keys.Escape)
            {
                if (_isRunning)
                {
                    _isRunning = false;
                    win.Close();
                    Console.WriteLine("press ESC quitStreamPreview");
                }
            }
            else if (key == Keys.S)
            {
                Console.WriteLine("syncDevicesTime...");
                _context?.EnableDeviceClockSync(60000);
            }
            else if (key == Keys.T)
            {
                Console.WriteLine("check software trigger mode");
                foreach (var dev in _streamDevList)
                {
                    var multiDeviceSyncConfig = dev.GetMultiDeviceSyncConfig();
                    if (multiDeviceSyncConfig.syncMode == MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_SOFTWARE_TRIGGERING)
                    {
                        Console.WriteLine("software trigger...");
                        dev.TriggerCapture();
                    }
                }
            }
        }

        static bool LoadConfigFile()
        {
            try
            {
                if (!File.Exists(ConfigFile))
                {
                    Console.WriteLine($"Config file not found: {ConfigFile}");
                    CreateSampleConfig();
                    return false;
                }

                var json = File.ReadAllText(ConfigFile);
                var config = JsonSerializer.Deserialize<MultiDeviceSyncConfigRoot>(json);

                if (config?.Devices != null)
                {
                    _deviceConfigList = config.Devices;
                    Console.WriteLine($"Loaded {_deviceConfigList.Count} device configs:");
                    int deviceCount = 0;
                    foreach (var dev in _deviceConfigList)
                    {
                        Console.WriteLine($"  config[{deviceCount++}]: SN={dev.DeviceSN}, mode={dev.SyncConfig.SyncMode}");
                    }
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Load config failed: {e.Message}");
                return false;
            }
        }

        static void CreateSampleConfig()
        {
            var sampleConfig = new MultiDeviceSyncConfigRoot
            {
                Devices = new List<DeviceConfigInfo>
                {
                    new DeviceConfigInfo
                    {
                        DeviceSN = "YOUR_DEVICE_SN_1",
                        SyncConfig = new SyncConfig
                        {
                            SyncMode = "OB_MULTI_DEVICE_SYNC_MODE_PRIMARY",
                            TriggerOutEnable = true
                        }
                    },
                    new DeviceConfigInfo
                    {
                        DeviceSN = "YOUR_DEVICE_SN_2",
                        SyncConfig = new SyncConfig
                        {
                            SyncMode = "OB_MULTI_DEVICE_SYNC_MODE_SECONDARY",
                            TriggerOutEnable = false
                        }
                    }
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(sampleConfig, options);
            File.WriteAllText(ConfigFile, json);
            Console.WriteLine($"Created sample config file: {ConfigFile}");
            Console.WriteLine("Please edit it with your actual device serial numbers.");
        }

        static MultiDeviceSyncMode StringToOBSyncMode(string modeString) => modeString switch
        {
            "OB_MULTI_DEVICE_SYNC_MODE_FREE_RUN" => MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_FREE_RUN,
            "OB_MULTI_DEVICE_SYNC_MODE_STANDALONE" => MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_STANDALONE,
            "OB_MULTI_DEVICE_SYNC_MODE_PRIMARY" => MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_PRIMARY,
            "OB_MULTI_DEVICE_SYNC_MODE_SECONDARY" => MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_SECONDARY,
            "OB_MULTI_DEVICE_SYNC_MODE_SECONDARY_SYNCED" => MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_SECONDARY_SYNCED,
            "OB_MULTI_DEVICE_SYNC_MODE_SOFTWARE_TRIGGERING" => MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_SOFTWARE_TRIGGERING,
            "OB_MULTI_DEVICE_SYNC_MODE_HARDWARE_TRIGGERING" => MultiDeviceSyncMode.OB_MULTI_DEVICE_SYNC_MODE_HARDWARE_TRIGGERING,
            _ => throw new ArgumentException($"Unrecognized sync mode: {modeString}")
        };

        static void Cleanup()
        {
            _isRunning = false;

            foreach (var holder in _pipelineHolders)
            {
                try { holder.StopStream(); holder.Dispose(); } catch { }
            }
            _pipelineHolders.Clear();

            _mjpegConverter?.Dispose();
            _framePairingManager?.Dispose();

            foreach (var dev in _streamDevList) dev?.Dispose();
            foreach (var dev in _configDevList) dev?.Dispose();
            _streamDevList.Clear();
            _configDevList.Clear();
            _deviceConfigList.Clear();
            _deviceTextureIndices.Clear();

            _context?.Dispose();

            Console.WriteLine("MultiDevicesSync sample exited.");
        }
    }
}
