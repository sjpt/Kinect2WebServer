/////// derived from https://github.com/peted70/kinectv2-webserver
// serialization changed to use standard Microsoft System.Runtime.Serialization.Json;
// WebSockets changed to use Fleck, with code from Kinect1  https://github.com/LightBuzz/Kinect-HTML5
// Extensively changed for support of more streams, and on request sending of data
// NOT optimized for multiple clients requesting similar data.
#pragma warning disable IDE0040, IDE0049, CA1805, CA1304, CA1305, IDE0008, IDE0055, IDE0058, IDE0048, IDE0011

using System;
using System.Collections.Generic;
using System.Numerics;
using IWebSocketConnection = Fleck.IWebSocketConnection;
using System.Threading;
using System.IO;
using System.Drawing;
// using Nicrosoft.Kinect;
// using Vector4 = System.Numerics.Vector4;
// using Capture = Microsoft.Azure.Kinect.Sensor.Capture;

namespace Kinect2WebServer {
    partial class Program {

        // static readonly Random rnd = new();

        //static ColorFrameData colorData = new();
        //static byte[] encodedBytes;

        protected static ushort[] DepthArr, DepthArrOld, DepthArrUnFilt, InfraredArr;  // depth as from Kinect, depth double buffered for changes
        protected static byte[] ColorData;
        protected static int width, height, rsize, colWidth, colHeight, colBytesPerPixel;
        protected static UInt32[] RGBArr;

        static float[] DepthArrFloat, InfraredArrFloat;  // temp for conversion, resused for infrared
        static byte[] TempByteArray, IndexArr;       // temp for data send, resused for infrared
        static float[] DepthArrBack;            // background depth
        static float[] DepthArrBad;             // damped measure of badness
        static float[] DepthArrGeom;            // Depth in Organic geometry
        static byte[] DepthArrEdge;             // Edge array
        static byte[] DepthArr4;                // used to transmit depth to client (currrent val, old val, 0, 1)
        static byte[] DepthChangeArr;             // change array
        static byte[] SegmentId;                // which segment this core point is in
        static byte[] BaseSegment;               // 'base' segment for each segment
        static int numSegments;                 // number of segments found

        public struct VecPlus {
            public VecPlus(Vector3 ppos, float pv) { pos = ppos; v = pv; }
            public Vector3 pos; public float v;
        }

        static int maxDepth = 12000;            // 2400; // max depth to use (pending proper background elimination; hardly ever used now ??? 19 Nov 2021
        static bool tad4background = true;      // control whether information sent for cbackground core points
        static bool filterback = true;          // control whether to filter out background
        static UInt16 maxd = 0, gmaxd = 0;      // max depth recorded in last frame, and globally. ?debug only?
        static float backDamp = 0.97F;          // damping factor to allow for gradual background buildup
        static float backExtra = 0.97F;         // distance in front of background to ignore
        static float backMin = 0F;              // minimum distance to accept as background
        static float baddamp = 0.9f;            // damping factor for bad
        static float changeDamp = 0.99F;        // damping for change value
        // static float twistDamp = 0.99F;         // damping for twist value
        static float minChange = 10;            // minumum change value to consider
        static float maxChange = 30;            // maximum change value to use
        static float minDepthExtra = 1.04F;      // samples between depth min and min*minDepthExtra will be considered for hit: applies to the raw depth values from sensor
        protected static bool doMedian = true;   // set to perform median filter on input
        static int edgeThresh = 20;             // edge threshold in mm
        static int depthChangeThresh = 200;     // threshold for depth change, in mm
        static float minGoodn = 0.8f;           // proportion of reliable hits < minGoodn rule this out. was integer, but this scales with coreStep
        static int depthGeomStep = 4;           // step for depth geometry
        protected static string devid;
        // static int irthresh2 = 0; // 600;              // low threshold for ir to use value
        // static int irthresh4 = 0; // 200;              // low threshold for ir to use value
        static bool oldtwist = false;               // set to true for new twist
        // static int Irthresh() { return is K2 ? irthresh2 : irthresh4;  }

        static Matrix4x4 kinCamMat = new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);  // matrix to allow for Kinect camera position

        // constants for conversion of x,y,z to 'real geometry. assume same conversion for x and y (looks very close)
        static double da;
        protected static double fovx, fovy;           // fov in x and y, set by init code (hardcoded for each device)
        static float iddx, iddy;            // device dependent multiplier constant for conversion to geometry, computed in Allocate()
        static double raysPerMx, raysPerMy;  // device dependent, rays per m at one metre
        static double raysPerSlotx, raysPerSloty, raysPerSlotxy; // rays per core slot at one m

        static bool flowReport = false;     // true to send status messages back to client
        static string lastMessage = "no messageSL so far";    // last messageSL received, typically messageSL currently being processed
                                                    // this will need to change when we give further away items more priority
        static int callnum = 0;
        static List<string> stagelist = new();
        static double laststage = Now();
        static readonly Dictionary<string, double> startstage = new();

        static void Stage(string k, bool done = false) {
            double t = Now();
            string dd = done ? "done" : "";
            stagelist.Add($"{k} {dd} @{t} dt = {t - laststage:#,##0.000}");
            if (done) {
                stagelist.Add($">>{k} @{t} dt = {t - startstage[k]:#,##0.000}");
            }
            laststage = t;
            startstage[k] = t;
        }
        static void StageEnd(string k) {
            Stage(k, true);
        }

        public static double Now() { return (DateTime.Now - new DateTime(1970, 1, 1)).TotalMilliseconds; }

        static protected bool _debug = false;
        static void Debug(string msg) {
            if (_debug)
                CWriteLine(msg);
        }

        public static void CWriteLine(string msg) {
            lock (_MessageLock) {
                Console.WriteLine(Thread.CurrentThread.ManagedThreadId + ": " + msg);
            }
        }

        private static object _MessageLock = new object();
        public static void CWriteLine(string msg, ConsoleColor col) {
            lock (_MessageLock) {
                Console.BackgroundColor = col;
                Console.WriteLine(Thread.CurrentThread.ManagedThreadId + ": " + msg);
                Console.ResetColor();
            }
        }

        static bool bestAzure; // , bestK2;
        // static bool bestOrbbec; 
        static void Main() {
            // Program.ORBTEST(); return;
            // Thread.CurrentThread.Name = "Main";
            Console.CancelKeyPress += Exit;
            AppDomain.CurrentDomain.DomainUnload += Exit;
            AppDomain.CurrentDomain.ProcessExit += Exit;

            bool found = false;

            // bool zok = ZED.makeZedCamera(); // note, this is slow if no camera available
            try {
                found = orbbec.Init("Main", null);     // test for Azure software
            } catch (Exception e) {
                CWriteLine("Orbbec init error", ConsoleColor.Red);
                CWriteLine(e.ToString());
            }



            if (!found) try {
                bestAzure = azure.Init("Main", null);     // test for Azure software
                found = bestAzure;
            } catch (Exception e) {
                CWriteLine("Azure init error", ConsoleColor.Red);
                CWriteLine(e.ToString());
            }
            //if (false) {
            //    if (!bestAzure) {
            //        bestK2 = kinect2.Init("Kinect2 init", null);        // K2 if no Azure
            //        if (!bestK2) {
            //            CWriteLine("Cannot open either Azure or K2"); Environment.Exit(1);
            //        }
            //    }
            //}

            // start AFTER Kinect init, otherwise we get thread issues beween main thread init and webserver thread requests
            InitializeConnection(); // initialize the websocket connection

            CWriteLine("Enter commands, 'exit' or 'q' then Enter to exit");
            while (true) {
                var l = Console.ReadLine();
                if (l is "exit" or "q" or null) break;
                try {
                    IWebSocketConnection socket = _clients.Count == 0 ? null : _clients[^1]; // [^1] gets last
                    DoMessage(l, socket);
                } catch(Exception e) {
                    CWriteLine("Exception attempting messageSL " + l + e);

                }
            }
            Exit();
            return;
        }
        static void Exit(object sender = null, EventArgs e = null) {
            CWriteLine("closing any Kinects open");
            Close();
            CWriteLine("exiting");
            //Environment.Exit(-1);
            //AppDomain.Unload(AppDomain.CurrentDomain);
        }

        /** Allocate arrays and prepare other details depending on device
         */
        static void Allocate(IWebSocketConnection socket) {
            if (width == 0)
                return;
            RawAllocate();
            CoreAllocate();
            foreach (var lsocket in _clients) {     // after allocation sets some details
                SendKinsize(lsocket);               // so the client knows too
            }
            SegmentId = new byte[coreWidth * coreHeight];                // which segment this core point is in
            BaseSegment = new byte[256];                                  // 'base' segment for each segment

            //backgroundStats = new BackgroundStats[rsize];
            //CWriteLine("background init asize " + rsize);
            //for (var i = 0; i<rsize; i++) { backgroundStats[i] = new(); backgroundStats[i].ii = i; }
            //CWriteLine("background init done asize " + rsize);

            // do conditionally in Rawllocate LoadBackground();   // establish old background at start if possible

            // constants for conversion of x,y,z to 'real geometry.
            da = Math.PI / 180;
            double angx = fovx / 2, angy = fovy / 2;
            double pixx = width / 2, pixy = height / 2;
            double tanx = Math.Tan(angx * da), tany = Math.Tan(angy * da);
            double dddx = pixx / tanx, dddy = pixy / tany;             // z distance of 'plane' in pixel units
            iddx = (float)(1 / dddx); iddy = (float)(1 / dddy);

            raysPerMx = pixx/tanx;
            raysPerMy = pixy/tany;
            raysPerSlotx = raysPerMx * gridX / coreWidth;
            raysPerSloty = raysPerMy * gridY / coreHeight;
            raysPerSlotxy = raysPerSlotx * raysPerSloty;
            CWriteLine("Allocate done");
            Info(socket);
        }

        /** convenience for write of arrays where Write overload does not exist */
        static void WriteBuffer<T>(string fid, T[] buffer) {
            Type tt = typeof(T);
            int size =                  // C# generics not that friendly to sizeof, this is inconvenient but safe?
                (tt == typeof(float)) ? sizeof(float) :
                (tt == typeof(ushort)) ? sizeof(ushort) :
                -999;
            if (size < 0) throw (new Exception($"Unsupported type {tt} to WriteBuffer"));
            var bw = new BinaryWriter(File.Open(fid, FileMode.Create));
            Buffer.BlockCopy(buffer, 0, TempByteArray, 0, buffer.Length * size);  // convert to byte ready for transfer
            bw.Write(TempByteArray);
            bw.Close();
        }

        static readonly string localappdir = Environment.GetEnvironmentVariable("LOCALAPPDATA") + "\\Organic";
        static string backfid;
    }
}

