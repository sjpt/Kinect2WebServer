using System;
using Microsoft.Azure.Kinect.Sensor;
using IWebSocketConnection = Fleck.IWebSocketConnection;
using System.Numerics;
using Microsoft.Kinect;
using System.Threading;
using System.Net.Sockets;
#pragma warning disable IDE0011 // Add braces

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Kinect2WebServer {

    internal class Azure : Program, ICaptureDevice {
        // these public ones should become less visible later
        public static string _lastAzuremsfr = "";
        public static bool UseAzureCalibration = true;     // use azure code for depth => 3d transformation

        private Calibration AzureCalibration;
        private DeviceConfiguration AzureConfig = new();
        private Device AzureDevice;
        private string AzureDepthMode;
        private bool AzureCamerasStarted;  // = false;

        public string Type() { return "Azure"; }


        public void NewmsfrI(IWebSocketConnection socket, string k = "depth infrared") {
            //// https://docs.microsoft.com/en-us/azure/kinect-dk/retrieve-images
            //AzureConfig.DepthMode = DepthMode.NFOV_2x2Binned;
            //AzureConfig.ColorFormat = ImageFormat.ColorBGRA32;
            //AzureConfig.ColorResolution = ColorResolution.R1080p;
            //AzureConfig.SynchronizedImagesOnly = true;
            //AzureDevice.Start Cameras(AzureConfig);
            // https://docs.microsoft.com/en-us/azure/kinect-dk/hardware-specification
            // DEPTH CAMERA SUPPORTED OPERATING MODES   
            // Mode    Resolution FoI FPS Operating range* Exposure time
            // NFOV unbinned	        640x576	    75°x65°	    0, 5, 15, 30	0.5 - 3.86 m	12.8 ms
            // NFOV 2x2 binned(SW)      320x288	    75°x65°	    0, 5, 15, 30	0.5 - 5.46 m	12.8 ms
            // WFOV 2x2 binned          512x512	    120°x120°	0, 5, 15, 30	0.25 - 2.88 m	12.8 ms
            // WFOV unbinned	        1024x1024	120°x120°	0, 5, 15	    0.25 - 2.21 m	20.3 ms
            // Passive IR	            1024x1024   N/A	        0, 5, 15, 30	N/A	            1.6 ms


            void ee(string msg) {
                string s = $"#error No ${msg} support for Azure";
                Send(socket, s);
                CWriteLine(s);
            }
            if (_lastAzuremsfr != k + AzureDepthMode) {
                AzureDevice.StopCameras(); AzureDevice.StopImu();
                AzureCamerasStarted = false;
            }
            AzureConfig = new();
            fovx = 75; fovy = 65; // TODO set properly

            DepthMode dm = DepthMode.NFOV_2x2Binned;
            if (AzureDepthMode.Contains("nu")) dm = DepthMode.NFOV_Unbinned;
            if (AzureDepthMode.Contains("nb")) dm = DepthMode.NFOV_2x2Binned;
            if (AzureDepthMode.Contains("wu")) { dm = DepthMode.WFOV_Unbinned; fovx = fovy = 120; }
            if (AzureDepthMode.Contains("wb")) { dm = DepthMode.WFOV_2x2Binned; fovx = fovy = 120; }
            if (AzureDepthMode.Contains("ir")) { dm = DepthMode.PassiveIR; fovx = fovy = 120; }
            AzureConfig.DepthMode = dm;
            AzureConfig.CameraFPS = dm == DepthMode.WFOV_Unbinned ? FPS.FPS15 : FPS.FPS30;
            AzureConfig.ColorResolution = ColorResolution.Off; // .R720p;
            AzureConfig.ColorFormat = ImageFormat.ColorYUY2;   // ColorBGRA32; bgra need extra camera processing, ???YUY2 will need buffer size changes???

            //AzureCalibration.DepthMode = AzureConfig.DepthMode;
            //AzureCalibration.ColorResolution = AzureConfig.ColorResolution;

            if (k.Contains("body")) ee("body");
            if (k.Contains("color")) ee("color");
            // if (k.Contains("infrared")) ee("infrared");
            if (k.Contains("bodyindex")) ee("bodyindex");
            CWriteLine("newmsfr Azure cameras started check " + AzureCamerasStarted);
            if (!AzureCamerasStarted) {
                AzureDevice.StartCameras(AzureConfig);
                AzureCalibration = AzureDevice.GetCalibration();
                AzureCamerasStarted = true;
                CWriteLine("newmsfr Azure cameras started: " + k);
                AzureDevice.StartImu();
            }
            Capture cap = AzureDevice.GetCapture();
            _firstCapture = true;
            Image depthcap = cap.Depth;
            if (depthcap == null) depthcap = cap.IR;
            if (depthcap != null) {
                width = depthcap.WidthPixels; height = depthcap.HeightPixels;
            } else {
                width = height = 1;
            }
            Image colourcap = cap.Color;
            if (colourcap != null) {
                colWidth = colourcap.WidthPixels; colHeight = colourcap.HeightPixels;
                colBytesPerPixel = (int)colourcap.Size / (colWidth * colHeight);
            } else {
                colWidth = colHeight = colBytesPerPixel = 1;
            }


            CWriteLine($"Azure size setup ok, w={width} h={height}, colw={colWidth} colh={colHeight}");

            _lastAzuremsfr = k + AzureDepthMode;


        }

        public bool OpenI() {
            NewmsfrI(null);
            return true;
        }

        public void CloseI() {
            if (AzureDevice != null) { AzureDevice.StopCameras(); AzureDevice.StopImu(); AzureCamerasStarted = false; } // is Azure = false; } // AzureDevice.Dispose(); AzureDevice = null;
        }

        private Capture AzureFrame;

        public bool GetCapture() {
            AzureFrame = AzureDevice.GetCapture();
            return AzureFrame != null;
        }
        public bool GetDepthData() {
            Image depthCap = AzureFrame.Depth;
            if (depthCap == null) return false;
            //depthCap.DeviceTimestamp
            TimeSpan ctime = depthCap.DeviceTimestamp;
            timeDelta = (float)((ctime - depthTime).TotalMilliseconds / 1000);
            depthTime = ctime;
            // var diff = depthCap.SystemTimestampNsec * 1.0 - DateTimeOffset.Now.ToUnixTimeSeconds();
            // long zzz = DateTimeOffset.Now.ToUnixTimeSeconds();

            Memory<ushort> depthPixels = depthCap.GetPixels<ushort>();
            // var depthArray = depthPixels.ToArray();
            (DepthArr, DepthArrOld) = (DepthArrOld, DepthArr); // swap depth buffers
            depthPixels.CopyTo(DepthArr);

            // handle Azure specific noise, values above the 'sensible' region
            for (int i = 0; i < DepthArr.Length; i++) if (DepthArr[i] > 8000)
                    DepthArr[i] = 0;
            return true;
        }

        public bool GetInfrared() {
            Image ir = AzureFrame.IR;
            if (ir == null) return false;
            Memory<ushort> irPixels = ir.GetPixels<ushort>();
            irPixels.CopyTo(InfraredArr);
            return true;
        }

        private int[] remap = new int[1];
        private float aaxl=0, aaxh, aaxc, aayl, aayh, aayc;
        // int colw = 1920, colh = 1080;
        int colw, colh;  // not just mjpeg


        public bool GetRGB() {
            Image col = AzureFrame.Color;
            if (col == null) return false;
            colw = col.WidthPixels; colh = col.HeightPixels;
            // Memory<UInt32> colPixels = col.GetPixels<UInt32>();
            // colPixels.CopyTo(RGBArr);
            UInt32[] colPixels = col.GetPixels<UInt32>().ToArray();

            var dep = AzureFrame.Depth;
            if (dep == null) return false;
            int w = dep.WidthPixels, h = dep.HeightPixels;

            if (aaxl == 0) {
                // Vector2? t;
                float aad = 40;
                aaxc = AzureCalibration.TransformColor2DToDepth2D(new Vector2(colw / 2, colh / 2), dep).Value.X;
                aayc = AzureCalibration.TransformColor2DToDepth2D(new Vector2(colw / 2, colh / 2), dep).Value.Y;
                aaxl = AzureCalibration.TransformColor2DToDepth2D(new Vector2(colw / 2 - aad, colh / 2), dep).Value.X;
                aaxh = AzureCalibration.TransformColor2DToDepth2D(new Vector2(colw / 2 + aad, colh / 2), dep).Value.X;
                aayl = AzureCalibration.TransformColor2DToDepth2D(new Vector2(colw / 2, colh / 2 - aad), dep).Value.Y;
                aayh = AzureCalibration.TransformColor2DToDepth2D(new Vector2(colw / 2, colh / 2 + aad), dep).Value.Y;
            }


            //// if mapping not yet established, establish it
            //if (remap.Length != w * h) {
            //    remap = new int[w * h];
            //    int k = 0;
            //    for (int y = 0; y < 1080; y++) {
            //        for (int x = 0; x < 1920; x++) {
            //            Vector2? t = AzureCalibration.TransformColor2DToDepth2D(new Vector2(x, y), dep);
            //            if (t != null)
            //                remap[(int)(t.Value.X + t.Value.Y * w)] = k;  // << same 
            //            k++;
            //        }
            //    }
            //}

            // int i = 0;
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    //uint v = colPixels[remap[i]];
                    //RGBArr[i++] = v;
                }
            }

            // AzureCalibration.
            return true;
        }

        // installed Kinect SDK: https://docs.microsoft.com/en-us/azure/kinect-dk/sensor-sdk-download
        // added Microsoft.Azure.Kinect.Sensor reference to project (C:\Program Files\Azure Kinect SDK v1.4.1\sdk\netstandard2.0\release\Microsoft.Azure.Kinect.Sensor.dll)
        // added ref to System.Memory, System.Private.CoreLib for result of GetPixels(), but then lots of conflicts
        // copied k4a.dll from C:\Program Files\Azure Kinect SDK v1.4.1\sdk\windows-desktop\x86\release\bin into C:\GoldsmithsSVN\aaorganicart\kinectv2-server\KinectWebServer

        //
        // Depth may be provided outside of the operating range indicated above.
        // It depends on an object's reflectivity.
        public bool Init(string message, IWebSocketConnection socket) {
            CWriteLine("Azure.Init() " + message);
            width = height = 0;
            AzureDepthMode = message;
            _lastAzuremsfr = "UNDEF";
            try {
                // https://docs.microsoft.com/en-us/azure/kinect-dk/find-then-open-device
                CWriteLine("check number of azure devices");
                int device_count = Device.GetInstalledCount();
                CWriteLine("number of azure devices " + device_count);
                if (device_count == 0) return false;
                for (int retry = 0; retry < 10; retry++) {
                    if (AzureDevice == null) {
                        try {
                            AzureDevice = Device.Open(0); // there doesn't seem to be a real Close, so reopen dangerous
                        } catch (Exception e) {
                            CWriteLine($"cannot open Azure, will retry. {retry} {e.Message}");
                            System.Threading.Thread.Sleep(200);
                        }
                    }
                }

                width = 1;
                CWriteLine("AzureDevice setup OK", ConsoleColor.DarkGreen);
            } catch (Exception e) {
                CWriteLine("Azure error: " + e.Message, ConsoleColor.Red);
                return false;
            }
            devid = "Azure" + message;
            activeDevice = this;
            if (message == "Main") return true;
            Newmsfr(socket);
            OpenI();
            return true;
        }

        public Vector3 Accel() {
            try {
                ImuSample imu = AzureDevice.GetImuSample(TimeSpan.FromMilliseconds(1));
                Vector3 acc = imu.AccelerometerSample;
                return acc;
            } catch (Exception) {
                return Vector3.Zero;
            }
        }

        static private float[] _calib = new float[1];  // cache of XY values for calibration

        /* Use Azure builtin transformation for basic camera if requested
        * (https://docs.microsoft.com/en-us/azure/kinect-dk/use-calibration-functions)
        * The Azure calculation allows for a 6degree tilt on the depth camera
        * (https://docs.microsoft.com/en-us/azure/kinect-dk/coordinate-systems)
        * and? what else ... even allowing for that it doesn't map ix = width/2 => mx = 0 etc.
        * It probably knows soenthing we don't?
        */
        public bool Calibration(float ix, float iy, float iz, out float mx, out float my, out float mz) {
            if (!UseAzureCalibration) { mx = my = mz = 0;  return false; }

            // cache the basic Azure calibration (all modes have different sizes, so size test eaziest
            // slight loss of precision for non-integer ix,iy
            int nl = width * height * 2;
            if (_calib.Length != nl) {
                Console.WriteLine("recalibrate: " + AzureDepthMode + " " + width + " " + height + " ..." + _calib.Length + "=>" + nl);
                _calib = new float[nl];
                for (int y = 0, i = 0; y < height; y++) {
                    for (int x = 0; x < width; x++) {
                        Vector3? xyzqc = AzureCalibration.TransformTo3D(new Vector2(x, y), 1, CalibrationDeviceType.Depth, CalibrationDeviceType.Depth);
                        // may be null, as sensor does not cover complete rectangle range
                        Vector3 xyz = xyzqc.HasValue ? (Vector3)xyzqc : Vector3.Zero;
                        _calib[i++] = xyz.X / 1000;
                        _calib[i++] = -xyz.Y / 1000;
                    }
                }
            }
            int ii = ((int)ix + (int)iy * width) * 2;
            mx = _calib[ii] * iz;
            my = _calib[ii+1] * iz;
            mz = -iz / 1000;

            //Vector3? xyzq = AzureCalibration.TransformTo3D(new Vector2(ix, iy), iz, CalibrationDeviceType.Depth, CalibrationDeviceType.Depth);

            //if (xyzq == null) {
            //    mx = my = mz = float.NaN;
            //} else {
            //    Vector3 xyz = (Vector3)xyzq;
            //    mx = xyz.X / 1000; my = -xyz.Y / 1000; mz = -xyz.Z / 1000;
            //}
            return true;
        }

    }   // class Azure
} // namespace 

