using Orbbec; // Targetting the official OrbbecSDK_DotNet namespace
using System;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using IWebSocketConnection = Fleck.IWebSocketConnection;

#pragma warning disable IDE0011 // Add braces
#pragma warning disable IDE0130 // Namespace does not match folder structure

/*
mainly generated from Azure.cs by Gemini. needed some changes
Count() => ProfileCount()
FrameSet => Frameset
GetData => GetDataPtr

uint <=> int in a few places

 ****/

namespace Kinect2WebServer {

    internal class OrbbecX : Program, ICaptureDevice {
        public static string _lastAzuremsfr = "";
        public static bool UseAzureCalibration = true;     

        private Pipeline OrbbecPipeline;
        private Config OrbbecConfig = new Config();
        private Device OrbbecDevice;
        private Context OrbbecContext;
        private string AzureDepthMode;
        private bool AzureCamerasStarted;  

        // Stream configurations using modern types
        private VideoStreamProfile depthProfile;
        private VideoStreamProfile irProfile;

        public string Type() { return "OrbbecFemtoBolt"; }

        public void NewmsfrI(IWebSocketConnection socket, string k = "depth infrared") {
            void ee(string msg) {
                string s = $"#error No ${msg} support for Orbbec";
                Send(socket, s);
                CWriteLine(s);
            }

            if (_lastAzuremsfr != k + AzureDepthMode) {
                if (OrbbecPipeline != null) {
                    OrbbecPipeline.Stop();
                }
                AzureCamerasStarted = false;
            }

            OrbbecConfig = new Config();
            fovx = 75; fovy = 65; 

            int depthWidth = 320, depthHeight = 288, fps = 30;
            
            if (AzureDepthMode.Contains("nu")) { depthWidth = 640; depthHeight = 576; fps = 30; }
            if (AzureDepthMode.Contains("nb")) { depthWidth = 320; depthHeight = 288; fps = 30; }
            if (AzureDepthMode.Contains("wu")) { depthWidth = 1024; depthHeight = 1024; fps = 15; fovx = fovy = 120; }
            if (AzureDepthMode.Contains("wb")) { depthWidth = 512; depthHeight = 512; fps = 30; fovx = fovy = 120; }
            if (AzureDepthMode.Contains("ir")) { depthWidth = 1024; depthHeight = 1024; fps = 30; fovx = fovy = 120; }

            // Modern SDK approach: Extract matching profile from List via GetProfile() indexer matching the device capabilities
            using (var profileList = OrbbecPipeline.GetStreamProfileList(SensorType.OB_SENSOR_DEPTH)) {
                if (profileList != null && profileList.ProfileCount() > 0) {    // ? was .Count()
                    for (int i = 0; i < profileList.ProfileCount(); i++) {
                        var prof = profileList.GetProfile(i).As<VideoStreamProfile>();
                        if (prof.GetWidth() == depthWidth && prof.GetHeight() == depthHeight && prof.GetFPS() == fps && prof.GetFormat() == Format.OB_FORMAT_Y16) {
                            depthProfile = prof;
                            OrbbecConfig.EnableStream(depthProfile);
                            break;
                        }
                    }
                    // Fallback to absolute default if explicit constraints are missed
                    if (depthProfile == null) {
                        depthProfile = profileList.GetProfile(0).As<VideoStreamProfile>();
                        OrbbecConfig.EnableStream(depthProfile);
                    }
                }
            }

            using (var irProfileList = OrbbecPipeline.GetStreamProfileList(SensorType.OB_SENSOR_IR)) {
                if (irProfileList != null && irProfileList.ProfileCount() > 0) {
                    for (int i = 0; i < irProfileList.ProfileCount(); i++) {
                        var prof = irProfileList.GetProfile(i).As<VideoStreamProfile>();
                        if (prof.GetWidth() == depthWidth && prof.GetHeight() == depthHeight && prof.GetFPS() == fps) {
                            irProfile = prof;
                            OrbbecConfig.EnableStream(irProfile);
                            break;
                        }
                    }
                }
            }

            if (k.Contains("body")) ee("body");
            if (k.Contains("color")) ee("color");
            if (k.Contains("bodyindex")) ee("bodyindex");

            CWriteLine("newmsfr Orbbec cameras started check " + AzureCamerasStarted);
            if (!AzureCamerasStarted) {
                OrbbecPipeline.Start(OrbbecConfig);
                AzureCamerasStarted = true;
                CWriteLine("newmsfr Orbbec cameras started: " + k);
                
                // Fixed IMU matching modern GetProfile(uint) patterns
                using (var accList = OrbbecPipeline.GetStreamProfileList(SensorType.OB_SENSOR_ACCEL)) {
                    if (accList != null && accList.ProfileCount() > 0) {
                        var accProfile = accList.GetProfile(0); // Modern API matching v2 core profiles
                        OrbbecConfig.EnableStream(accProfile);
                    }
                }
            }

            using (Frameset testSet = OrbbecPipeline.WaitForFrames(1000)) {
                if (testSet != null) {
                    using (var dFrame = testSet.GetDepthFrame()) {
                        if (dFrame != null) {
                            width = (int)dFrame.GetWidth();
                            height = (int)dFrame.GetHeight();
                        } else {
                            width = height = 1;
                        }
                    }
                }
            }

            colWidth = colHeight = colBytesPerPixel = 1;
            CWriteLine($"Orbbec size setup ok, w={width} h={height}, colw={colWidth} colh={colHeight}");
            _lastAzuremsfr = k + AzureDepthMode;
        }

        public bool OpenI() {
            NewmsfrI(null);
            return true;
        }

        public void CloseI() {
            if (OrbbecPipeline != null) { 
                OrbbecPipeline.Stop(); 
                AzureCamerasStarted = false; 
            }
        }

        private Frameset OrbbecFrameSet;

        public bool GetCapture() {
            if (OrbbecFrameSet != null) OrbbecFrameSet.Dispose();
            OrbbecFrameSet = OrbbecPipeline.WaitForFrames(100); 
            return OrbbecFrameSet != null;
        }

        public bool GetDepthData() {
            if (OrbbecFrameSet == null) return false;

            using (var depthCap = OrbbecFrameSet.GetDepthFrame()) {  // was DepthFrame, not var
                if (depthCap == null) return false;
                
                TimeSpan ctime = TimeSpan.FromMilliseconds(depthCap.GetTimeStamp());
                timeDelta = (float)((ctime - depthTime).TotalMilliseconds / 1000);
                depthTime = ctime;

                int dataSize = (int)depthCap.GetDataSize();
                (DepthArr, DepthArrOld) = (DepthArrOld, DepthArr);
                
                unsafe {
                    ushort* ptr = (ushort*)depthCap.GetDataPtr().ToPointer();
                    fixed (ushort* destPtr = DepthArr) {
                        Buffer.MemoryCopy(ptr, destPtr, DepthArr.Length * 2, dataSize);
                    }
                }

                for (int i = 0; i < DepthArr.Length; i++) {
                    if (DepthArr[i] > 8000) DepthArr[i] = 0;
                }
                return true;
            }
        }

        public bool GetInfrared() {
            if (OrbbecFrameSet == null) return false;

            using (IRFrame irFrame = OrbbecFrameSet.GetIRFrame()) {
                if (irFrame == null) return false;
                
                unsafe {
                    ushort* ptr = (ushort*)irFrame.GetDataPtr().ToPointer();
                    fixed (ushort* destPtr = InfraredArr) {
                        Buffer.MemoryCopy(ptr, destPtr, InfraredArr.Length * 2, irFrame.GetDataSize());
                    }
                }
                return true;
            }
        }

        public bool GetRGB() { return false; }

        public bool Init(string message, IWebSocketConnection socket) {
            CWriteLine("Orbbec.Init()");
            width = height = 0;
            AzureDepthMode = message;
            _lastAzuremsfr = "UNDEF";
            
            try {
                OrbbecContext = new Context();
                using (var deviceList = OrbbecContext.QueryDeviceList()) {
                    uint device_count = deviceList.DeviceCount();
                    CWriteLine("number of Orbbec devices found: " + device_count);
                    if (device_count == 0) return false;

                    OrbbecDevice = deviceList.GetDevice(0);
                }
                
                OrbbecPipeline = new Pipeline(OrbbecDevice);
                width = 1;
                CWriteLine("OrbbecDevice pipeline setup OK", ConsoleColor.DarkGreen);
            } catch (Exception e) {
                CWriteLine("Orbbec Native SDK initialization error: " + e.Message, ConsoleColor.Red);
                return false;
            }

            devid = "Orbbec_" + message;
            activeDevice = this;
            if (message == "Main") return true;
            Newmsfr(socket);
            OpenI();
            return true;
        }

        public Vector3 Accel() {
            if (OrbbecFrameSet == null) return Vector3.Zero;
            
            using (AccelFrame accFrame = OrbbecFrameSet.GetFrame(FrameType.OB_FRAME_ACCEL)?.As<AccelFrame>()) {
                if (accFrame != null) {
                    var v = accFrame.GetAccelValue(); // changed from Gemini
                    // Vector extraction matches struct values exposed via the native bindings
                    return new Vector3(v.x, v.y, v.z);
                }
            }
            return Vector3.Zero;
        }

        static private float[] _calib = new float[1];  

        public bool Calibration(float ix, float iy, float iz, out float mx, out float my, out float mz) {
            if (!UseAzureCalibration) { mx = my = mz = 0; return false; }

            int nl = width * height * 2;
            if (_calib.Length != nl) {
                Console.WriteLine("recalibrate Orbbec coordinate maps: " + AzureDepthMode + " " + width + " " + height);
                _calib = new float[nl];
                
                // Fetch direct Camera Parameter Intrinsic configurations
                var calibrationParam = OrbbecPipeline.GetCameraParam();

                // looping version for older SDKs, replaced by PointCloudFilter for modern Orbbec SDK
                //int i = 0;
                //for (int y = 0; y < height; y++) {
                //    for (int x = 0; x < width; x++) {
                //        Point2f pixel = new Point2f { x = x, y = y };

                //        // Converts modern types inside CoordinateTransform bindings natively
                //        if (CoordinateTransform.DepthTo3D(calibrationParam, pixel, 1.0f, out Point3f point3D)) {
                //            _calib[i++] = point3D.x / 1000f;
                //            _calib[i++] = -point3D.y / 1000f;
                //        } else {
                //            _calib[i++] = 0;
                //            _calib[i++] = 0;
                //        }
                //    }
                //}
                //
                // pending here, should not call complete transfxormation for each point
                var pc = new PointCloudFilter();
                // var points = pc.Process(depthFrame);
                //??? ....

            }
            
            int ii = ((int)ix + (int)iy * width) * 2;
            mx = _calib[ii] * iz;
            my = _calib[ii + 1] * iz;
            mz = -iz / 1000f;

            return true;
        }
    }
}



/**************************~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ dead below here, remove soonish 12 July 2026
using Fleck;
using MathNet.Numerics.Distributions;
//using Microsoft.Azure.Kinect.Sensor;
//using Microsoft.Kinect;
using Orbbec; // Use native Orbbec SDK wrapper instead of Microsoft.Orbbec.Kinect.Sensor
using sl;
using System;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using IWebSocketConnection = Fleck.IWebSocketConnection;

#pragma warning disable IDE0011 // Add braces
#pragma warning disable IDE0130 // Namespace does not match folder structure

namespace Kinect2WebServer {
// namespace Orbbec {

    internal class OrbbecX : Program, ICaptureDevice {
        public static string _lastAzuremsfr = "";
        public static bool UseAzureCalibration = true;

        private Pipeline OrbbecPipeline;
        private Config OrbbecConfig = new Config();
        private Device OrbbecDevice;
        private Context OrbbecContext;
        private string AzureDepthMode;
        private bool AzureCamerasStarted;

        // Orbbec hardware configuration variables
        private StreamProfile depthProfile;
        private StreamProfile irProfile;
        private StreamProfile colorProfile;

        public string Type() { return "OrbbecFemtoBolt"; }

        public void NewmsfrI(IWebSocketConnection socket, string k = "depth infrared") {
            void ee(string msg) {
                string s = $"#error No ${msg} support for Orbbec";
                Send(socket, s);
                CWriteLine(s);
            }

            if (_lastAzuremsfr != k + AzureDepthMode) {
                if (OrbbecPipeline != null) {
                    OrbbecPipeline.Stop();
                }
                AzureCamerasStarted = false;
            }

            OrbbecConfig = new Config();
            fovx = 75; fovy = 65;

            // Map Orbbec Kinect profiles to Femto Bolt ToF profiles
            // Orbbec SDK utilizes explicit Sensor type resolution configurations
            int depthWidth = 320, depthHeight = 288, fps = 30;

            if (AzureDepthMode.Contains("nu")) { depthWidth = 640; depthHeight = 576; fps = 30; }
            if (AzureDepthMode.Contains("nb")) { depthWidth = 320; depthHeight = 288; fps = 30; }
            if (AzureDepthMode.Contains("wu")) { depthWidth = 1024; depthHeight = 1024; fps = 15; fovx = fovy = 120; }
            if (AzureDepthMode.Contains("wb")) { depthWidth = 512; depthHeight = 512; fps = 30; fovx = fovy = 120; }
            if (AzureDepthMode.Contains("ir")) { depthWidth = 1024; depthHeight = 1024; fps = 30; fovx = fovy = 120; }

            // Fetch the stream profiles from the open device
            var profileList = OrbbecPipeline.GetStreamProfileList(SensorType.OB_SENSOR_DEPTH);
            if (profileList != null) {
                // Find matching resolution and frame rate
                depthProfile = profileList.GetVideoStreamProfile(depthWidth, depthHeight, Format.OB_FORMAT_Y16, fps);
                if (depthProfile != null) {
                    OrbbecConfig.EnableStream(depthProfile);
                }
            }

            // Always configure Infrared streams matching the specified depth configuration
            var irProfileList = OrbbecPipeline.GetStreamProfileList(SensorType.OB_SENSOR_IR);
            if (irProfileList != null) {
                irProfile = irProfileList.GetVideoStreamProfile(depthWidth, depthHeight, Format.OB_FORMAT_Y16, fps);
                if (irProfile != null) {
                    OrbbecConfig.EnableStream(irProfile);
                }
            }

            if (k.Contains("body")) ee("body");
            if (k.Contains("color")) ee("color");
            if (k.Contains("bodyindex")) ee("bodyindex");

            CWriteLine("newmsfr Orbbec cameras started check " + AzureCamerasStarted);
            if (!AzureCamerasStarted) {
                OrbbecPipeline.Start(OrbbecConfig);
                AzureCamerasStarted = true;
                CWriteLine("newmsfr Orbbec cameras started: " + k);

                // Enable IMU accelerometer data streaming natively via config settings if required
                var accList = OrbbecPipeline.GetStreamProfileList(SensorType.OB_SENSOR_ACCEL);
                if (accList != null) {
                    var accProfile = accList.GetStreamProfile(0); 
                    // var accProfile = accList.GetAccelStreamProfile(0);
                    OrbbecConfig.EnableStream(accProfile);
                }
            }

            // Test frame acquisition to calculate initialization bounds
            using (FrameSet testSet = OrbbecPipeline.WaitForFrames(1000)) {
                if (testSet != null) {
                    using (DepthFrame dFrame = testSet.GetDepthFrame()) {
                        if (dFrame != null) {
                            width = (int)dFrame.GetWidth();
                            height = (int)dFrame.GetHeight();
                        } else {
                            width = height = 1;
                        }
                    }
                }
            }

            colWidth = colHeight = colBytesPerPixel = 1; // Default fallback matching color off config

            CWriteLine($"Orbbec size setup ok, w={width} h={height}, colw={colWidth} colh={colHeight}");
            _lastAzuremsfr = k + AzureDepthMode;
        }

        public bool OpenI() {
            NewmsfrI(null);
            return true;
        }

        public void CloseI() {
            if (OrbbecPipeline != null) {
                OrbbecPipeline.Stop();
                AzureCamerasStarted = false;
            }
        }

        private FrameSet OrbbecFrameSet;

        public bool GetCapture() {
            if (OrbbecFrameSet != null) OrbbecFrameSet.Dispose();

            // Replaces AzureDevice.GetCapture()
            OrbbecFrameSet = OrbbecPipeline.WaitForFrames(100);
            return OrbbecFrameSet != null;
        }

        public bool GetDepthData() {
            if (OrbbecFrameSet == null) return false;

            using (DepthFrame depthCap = OrbbecFrameSet.GetDepthFrame()) {
                if (depthCap == null) return false;

                // Read frame timestamp metadata
                TimeSpan ctime = TimeSpan.FromMilliseconds(depthCap.GetTimeStamp());
                timeDelta = (float)((ctime - depthTime).TotalMilliseconds / 1000);
                depthTime = ctime;

                int dataSize = (int)depthCap.GetDataSize();
                (DepthArr, DepthArrOld) = (DepthArrOld, DepthArr); // Swap buffers safely

                // Direct memory copy using pointers or buffer extraction depending on runtime array types
                unsafe {
                    ushort* ptr = (ushort*)depthCap.GetData().ToPointer();
                    fixed (ushort* destPtr = DepthArr) {
                        Buffer.MemoryCopy(ptr, destPtr, DepthArr.Length * 2, dataSize);
                    }
                }

                // Handle noise thresholding matching legacy application requirements
                for (int i = 0; i < DepthArr.Length; i++) {
                    if (DepthArr[i] > 8000) DepthArr[i] = 0;
                }
                return true;
            }
        }

        public bool GetInfrared() {
            if (OrbbecFrameSet == null) return false;

            using (IRFrame irFrame = OrbbecFrameSet.GetIRFrame()) {
                if (irFrame == null) return false;

                unsafe {
                    ushort* ptr = (ushort*)irFrame.GetData().ToPointer();
                    fixed (ushort* destPtr = InfraredArr) {
                        Buffer.MemoryCopy(ptr, destPtr, InfraredArr.Length * 2, irFrame.GetDataSize());
                    }
                }
                return true;
            }
        }

        public bool GetRGB() {
            // Placeholder matching original code constraints where ColorResolution was off
            return false;
        }

        public bool Init(string message, IWebSocketConnection socket) {
            CWriteLine("Orbbec.Init()");
            width = height = 0;
            AzureDepthMode = message;
            _lastAzuremsfr = "UNDEF";

            try {
                OrbbecContext = new Context();
                var deviceList = OrbbecContext.QueryDeviceList();
                int device_count = deviceList.DeviceCount();
                CWriteLine("number of Orbbec devices found: " + device_count);

                if (device_count == 0) return false;

                OrbbecDevice = deviceList.GetDevice(0);
                OrbbecPipeline = new Pipeline(OrbbecDevice);

                width = 1;
                CWriteLine("OrbbecDevice pipeline setup OK", ConsoleColor.DarkGreen);
            } catch (Exception e) {
                CWriteLine("Orbbec Native SDK initialization error: " + e.Message, ConsoleColor.Red);
                return false;
            }

            devid = "Orbbec_" + message;
            activeDevice = this;
            if (message == "Main") return true;
            Newmsfr(socket);
            OpenI();
            return true;
        }

        public Vector3 Accel() {
            if (OrbbecFrameSet == null) return Vector3.Zero;

            using (AccelFrame accFrame = OrbbecFrameSet.GetFrame(FrameType.OB_FRAME_ACCEL) as AccelFrame) {
                if (accFrame != null) {
                    var data = accFrame.GetTemperature(); // Example structural fetch if explicit struct maps lack fields
                    // Switch to target internal Accel data maps:
                    return new Vector3(accFrame.X, accFrame.Y, accFrame.Z);
                }
            }
            return Vector3.Zero;
        }

        static private float[] _calib = new float[1];

        public bool Calibration(float ix, float iy, float iz, out float mx, out float my, out float mz) {
            if (!UseAzureCalibration) { mx = my = mz = 0; return false; }

            int nl = width * height * 2;
            if (_calib.Length != nl) {
                Console.WriteLine("recalibrate Orbbec coordinate maps: " + AzureDepthMode + " " + width + " " + height);
                _calib = new float[nl];

                // Extract camera intrinsic parameters directly from Orbbec Pipeline maps
                var calibrationParam = OrbbecPipeline.GetCameraParam();

                int i = 0;
                for (int y = 0; y < height; y++) {
                    for (int x = 0; x < width; x++) {
                        // Replaces Orbbec's TransformTo3D utility maps natively using internal intrinsic projections
                        Point2f pixel = new Point2f { x = x, y = y };
                        Point3f point3D;

                        // Project 2D Depth map to 3D Space coordinates using camera parameter configurations
                        if (CoordinateTransform.DepthTo3D(calibrationParam, pixel, 1.0f, out point3D)) {
                            _calib[i++] = point3D.x / 1000f;
                            _calib[i++] = -point3D.y / 1000f;
                        } else {
                            _calib[i++] = 0;
                            _calib[i++] = 0;
                        }
                    }
                }
            }

            int ii = ((int)ix + (int)iy * width) * 2;
            mx = _calib[ii] * iz;
            my = _calib[ii + 1] * iz;
            mz = -iz / 1000f;

            return true;
        }
    }
}
*****************/