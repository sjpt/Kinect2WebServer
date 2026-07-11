// 21 Dev 2023, commented out, something wrong with getting stereolabs.zed on Stephen's laptop
// 15 June 2024, reallowed, but not tested widely yet ...
// Stereolabs.zed also removed for dependencies~packages
using System;
using System.ComponentModel;
using Fleck;
using sl;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Kinect2WebServer {
    internal class ZED : Program, ICaptureDevice {
        private Camera zedCamera;
        private Mat depthMap;
        private RuntimeParameters runtimeParameters;
        private Resolution res;

        public string Type() { return "zed"; }

        public void CloseI() {
            zedCamera.DisablePositionalTracking();
            // ??? depthMap.Free(); // sometimes gives error; as we don't do depthMat.Alloc maybe not needed?
            zedCamera.Close();
        }

        public bool GetCapture() {
            ERROR_CODE e = zedCamera.Grab(ref runtimeParameters);
            if (e != ERROR_CODE.SUCCESS)
                e = e+0;
            return e == ERROR_CODE.SUCCESS;
        }

        public bool GetDepthData() {
            ERROR_CODE e = zedCamera.RetrieveMeasure(depthMap, MEASURE.DEPTH); //, MEM.CPU, res);
            if (e != ERROR_CODE.SUCCESS) {
                Console.WriteLine("ZED RetrieveMeasure bad error {e}");
                return false;
            }
            int p = 0; int n0 = 0, nnan = 0, ninf = 0, nok = 0, nerr = 0;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    e = depthMap.GetValue(x, y, out float v);
                    if (e != ERROR_CODE.SUCCESS) {
                        nerr++;
                    } else if (v == 0) {
                        n0++;
                    } else if (float.IsNaN(v)) {
                        nnan++;
                    } else if (float.IsInfinity(v)) {
                        ninf++;
                        v = 1200;
                    } else {
                        nok++;
                    }
                    DepthArr[p++] = (ushort)v;
                }
            }
            return true;
        }

        public bool Init(string message, IWebSocketConnection socket) {
            // Set configuration parameters
            // see https://www.stereolabs.com/docs/depth-sensing/depth-settings/
            InitParameters init_params = new();
            //init_params.resolution = RESOLUTION.VGA; //  VGA is lowest
            //init_params.cameraFPS = 30;
            //init_params.depthMode = DEPTH_MODE.QUALITY; // ?ULTRA
            //init_params.coordinateUnits = UNIT.MILLIMETER;  // ?METER;
            //init_params.coordinateSystem = COORDINATE_SYSTEM.RIGHT_HANDED_Y_UP;
            //init_params.depthMaximumDistance = 5000f;
            //init_params.depthMinimumDistance = 500f;

            init_params.resolution = RESOLUTION.VGA; //  VGA is lowest
            init_params.cameraFPS = 30;
            init_params.depthMode = DEPTH_MODE.PERFORMANCE; // ?ULTRA
            init_params.coordinateUnits = UNIT.MILLIMETER;  // ?METER;
            init_params.coordinateSystem = COORDINATE_SYSTEM.RIGHT_HANDED_Y_UP;
            init_params.depthMaximumDistance = 50000f;


            // parseArgs(args, ref init_params);
            // Open the camera
            zedCamera = new Camera(0);
            ERROR_CODE err = zedCamera.Open(ref init_params);
            if (err != ERROR_CODE.SUCCESS) {
                Console.WriteLine("Error opening ZED camera: " + err);
                return false;
            }

            // may move this to newmsfri ???
            PositionalTrackingParameters trackingParams = new();
            _ = zedCamera.EnablePositionalTracking(ref trackingParams);

            runtimeParameters = new RuntimeParameters();

            depthMap = new Mat();
            height = zedCamera.ImageHeight;
            width = zedCamera.ImageWidth;
            fovy = zedCamera.GetFOV() * 180 / Math.PI;
            fovx = 110;     // TODO
            colWidth = width;
            colHeight = height;
            colBytesPerPixel = 3;

            res = new Resolution((uint)width, (uint)height);
            depthMap.Create(res, MAT_TYPE.MAT_32F_C1, MEM.CPU); // nb Mat_32F_C1 for depth data

            activeDevice = this;
            Newmsfr(socket);

            return false;
        }

        public void NewmsfrI(IWebSocketConnection socket, string k = "depth infrared") {
            // ???? nothing to do here, already done in Init and nothing else changes????
            // throw new NotImplementedException();
        }

        public bool OpenI() {
            // ???? nothing to do here, already done in Init and nothing else changes????
            // May want to move some code from Init
            // throw new NotImplementedException();
            return true;
        }
    }
}
/* Notes;
 * https://www.stereolabs.com/docs/app-development/csharp/use/
 * 
Examples saved in e.g. D:\dev\zed-examples-master\depth sensing\csharp
Installed stereolabs.zed into this project using Visual Studio nuget (right click in project, manage nuget packages)

Generated and compiled D:\dev\zed-examples-master\depth sensing\csharp using cmake (used VS 16)
Opened under VS17, works ok

 * */
