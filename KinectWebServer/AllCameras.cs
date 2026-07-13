#pragma warning disable IDE0040, IDE0049, CA1805, CA1304, CA1305, IDE0008, IDE0055, IDE0058, IDE0048, IDE0011

// generic program to manage different CaptureDevice
// see also Program.cs for startup sequence (Main())
// and Sender.cs for explicit commands, some of which are specific camera related (DoMessage(), line 181 etc)
using Microsoft.Kinect;
using System;
using System.IO;
using IWebSocketConnection = Fleck.IWebSocketConnection;
using System.Numerics;


namespace Kinect2WebServer {

    interface ICaptureDevice {
        public abstract string Type();
        public abstract void NewmsfrI(IWebSocketConnection socket, string k = "depth infrared");
        public abstract bool OpenI();
        public abstract void CloseI();
        public abstract bool GetCapture();
        public abstract bool GetDepthData();
        public abstract bool Init(string message, IWebSocketConnection socket);
        public bool GetInfrared() { return false; }
        public bool GetIndex() { return false; }
        public bool GetSkeleton() { return false; }
        public bool GetRGB() { return false; }
        public bool Calibration(float ix, float iy, float iz, out float mx, out float my, out float mz) { mx = my = mz = 0; return false; }
        public Vector3 Accel() { return Vector3.Zero;  }
    }


    partial class Program {


        static readonly ICaptureDevice azure = new Azure();
        static readonly ICaptureDevice kinect2 = new Kinect2();
        static readonly ICaptureDevice zed = new ZED();     // commented out, Stephen 21 Dec 2023, readded 15 June 2024
        static readonly ICaptureDevice orbbec = new OrbbecX();     // commented out, Stephen 21 Dec 2023, readded 15 June 2024
        protected static ICaptureDevice activeDevice = null;

        // static Nicrosoft.Kinect.KinectSensor k1sensor = Nicrosoft.Kinect.KinectSensor.KinectSensors.SingleOrDefault();

        // static bool is K2 = false, is Azure = false, isZed = false;

        protected static bool _open = false;
        protected static bool _firstCapture = true;			// prevent attempt for first get capture too soon after resolution find capture


        /** restart framereader with new options. mostly get 30fps, adding color reduces it a lot (around 15fps), others don't make much (any?) difference */
        protected static void Newmsfr(IWebSocketConnection socket, string k = "depth infrared") {
            CWriteLine($"newmsfr {k}, device {activeDevice.Type()}");
            activeDevice.NewmsfrI(socket, k);
            Send(socket, "#newmsfr");
            Allocate(socket);
        }

        static void Info(IWebSocketConnection socket) {
            string str = $"raw {width}x{height}={width*height} @ {coreStepn} => core {coreWidth}x{coreHeight}={coreWidth*coreHeight} clients {_clients.Count}";
            CWriteLine(str);
            if (socket != null) socket.Send("#info: " + str);
        }


        protected static bool Open() {
            activeDevice.OpenI();
            return true;
        }
        protected static void Close() {
            if (activeDevice != null) activeDevice.CloseI();
            // is Azure = is K2 = isZed = false;
        }

        static void SendKinsize(IWebSocketConnection socket) {
            Send(socket, $"kinsize {width} {height} {coreStepn} {coreWidth} {coreHeight}");
        }

        /** acquire data and send back to socket, may be multiple sources depending on the get message arguments */
        static void AcquireSendData(IWebSocketConnection socket, string message) {
            if (activeDevice == null) return;
            Stage("SendData");
            lastMessage = message;
            if (DepthArr == null) { 
                // CWriteLine("premature get/AcquireSendData ignored");
                Send(socket, $"#flowdone kind_not_ready time {depthTime} delta {timeDelta}");
                return;
            }

            if (socket == null) CWriteLine("attempt to SendData to non-socket");
            void report(string tag) {
                if (flowReport && socket != null) Send(socket, "#" + tag);
            }
            report("toprocess");

            bool depthok, infraredok, skelok = false, rgbok = false, indexok = false, newframe = false;

            if (_firstCapture) { _firstCapture = false; return; }
            try { 
                newframe = activeDevice.GetCapture();
            } catch (Exception e) {
                CWriteLine("AzureDevice.GetCapture() error: " + e.Message);
            }
            if (!newframe) { Send(socket, "#flowdone nonewdata"); Stage("AcquiredLatestFrameBad"); return; }
            Stage("AcquiredLatestFrame");
            callnum++;
            //always get depth and infrared
            //if (message.Contains("depth") || message.Contains("core") || message.Contains("depthsingle") || message.Contains("grid")) depthok = GetDepthData(k2frame, AzureFrame);
            //if (message.Contains("infrared")) infraredok = GetInfrared(k2frame, AzureFrame);
            depthok = activeDevice.GetDepthData();
            infraredok = activeDevice.GetInfrared();
            if (message.Contains("skel")) skelok = activeDevice.GetSkeleton();    // when we have skeleton/colour/index working for Azure add AzureFrame as secon d parameter
            if (message.Contains("rgb")) rgbok = activeDevice.GetRGB();
            if (message.Contains("index")) indexok = activeDevice.GetIndex();
            Stage("GotBaseData");
            report("kindata_read");


            // k2frame, AzureFrame = null; // no Close or Dispose, and can't be used in using statement
            if (!depthok || !infraredok) {
                Debug("bad depth or infrared");
            }
            TrySaveDIr();

            // now format as needed and send
            if (depthok) {
                RefineBackground();
                if (message.Contains("core") || message.Contains("depthsingle") || message.Contains("grid")) CoreReduce();

                if (message.Contains("depthsingle")) ReduceSingle(socket);
                if (message.Contains("coreposition")) SendDepthBlock(socket, CorePosition, "coreposition"); //ccc[7] = CountGood();
                if (message.Contains("coretwist")) SendDepthBlock(socket, CoreTwist, "coretwist");
                if (message.Contains("corevelocity")) SendDepthBlock(socket, CoreVelocity, "corevelocity");
                if (message.Contains("coreoffset")) Send(socket, "coreoffset", CoreOffset);
                if (message.Contains("corecolours")) Send(socket, "corecolours", CoreColours);
                if (message.Contains("coreedges")) { Send(socket, "coreedges"); Send(socket, CoreEdges); }
                if (message.Contains("depthgeometry")) SendDepthGeometry(socket); // includes geometry, plus hits/change
                if (message.Contains("deptharrbad")) { Send(socket, "deptharrbad"); Send(socket, DepthArrBad); }
                if (message.Contains("depthraw")) SendDepthRaw(socket);
                if (message.Contains("depthnoback")) SendDepthNoback(socket);
                if (message.Contains("depthback")) SendDepthBack(socket);
                if (message.Contains("depthedge")) SendDepthEdge(socket);
                if (message.Contains("edgegeom")) SendEdgeGeom(socket);
                if (message.Contains("depth4")) SendDepth4(socket);
                if (message.Contains("index")) SendIndex(socket);
                if (message.Contains("depthchange")) SendDepthChange(socket);
                if (message.Contains("segment")) SendSegment(socket);
                if (message.Contains("segstats")) SendSegstats(socket);
                Grid.GridMessage(socket, message);
            }
            if (infraredok && message.Contains("infrared")) SendInfrared(socket);
            if (skelok) SendSkeleton(socket);
            if (rgbok) SendRGB(socket);
            if (indexok) SendIndex(socket);
            Send(socket, $"#flowdone kindata_sent time {depthTime} delta {timeDelta}");

            // k2frame, AzureFrame.Close(); // neither close nor dispose exists here?
        }

        protected static TimeSpan depthTime;
        protected static float timeDelta;

        protected static string _savedir = "";
        /** save depth and infrared if requested */
        static void TrySaveDIr() {
            if (_savedir == "") return;
            WriteBuffer(_savedir + ".depth", DepthArr);
            WriteBuffer(_savedir + ".ir", InfraredArr);
            _savedir = "";
        }

        /** send the rgb data to client (incomplete) */
        private static void SendRGB(IWebSocketConnection socket) {
            //frame.CopyConvertedFrameDataToArray(ColorData, ColorImageFormat.Rgba);
            Send(socket, "colordata");
            socket.Send(ColorData);
        }

        // median filter, assume width*height, ignore outside for speed
        protected static void MedianFilter(ushort[] ia, ushort[] oa) {
            // long st = System.Diagnostics.Stopwatch.GetTimestamp();

            for (int y = 1; y < height - 1; y++) {
                for (int x = 1; x < width - 1; x++) {
                    int i = x + y * width;
                    ushort cc = ia[i];
                    ushort a = cc;
                    ushort b = ia[i + width];
                    ushort c = ia[i - width];
                    ushort d = ia[i + 1];
                    ushort e = ia[i - 1];
                    //ushort v = MedianOfFive(a[0], a[1], a[2], a[3], a[4]); // can't use MedianOfFive because of our special case 0 values
                    //DepthArrFloat[i] = v * k; // to test with no median
                    //!!var arr = new[] { a, b, c, d, e }; Array.Sort(arr); //!! use original Sort to double check code below

                    // timing info. having a,b,c,d,e as array a[5] and using regular Sort(a), around 11/12 ms
                    // using Sort5 as separate  function with call, around 9/10 ms
                    // Sort5 inline, around 6/7ms
                    //
                    //(a,b,c,d,e) = Sort5(a,b,c,d,e);                   // use optimized 5 item sort,
                    // inline optimized 5 item sort below.
                    // This can be marginally simplified as smallest sorted value is irrelevant to final median result
                    // As pointed out by the optimizer, but the optimizer does it anyway so no time saved, and it adds a little confusion.

#pragma warning disable IDE0059 // Unnecessary assignment of a value
#pragma warning disable CS1717 // Assignment made to same variable
                    if (a < b) (a, b) = (b, a);                         //    if a < b:      a, b = b, a
                    if (c < d) (c, d) = (d, c);                         //    if c < d:      c, d = d, c
                    if (a < c) (a, b, c, d) = (c, d, a, b);             //    if a < c:      a, b, c, d = c, d, a, b
                    if (e < c)                                          //    if e < c:
                        if (e < d) { } // pass                          //        if e < d:  pass
                        else (d, e) = (e, d);                           //        else:      d, e = e, d
                    else                                                //    else:
                        if (e < a) (c, d, e) = (e, c, d);               //        if e < a:  c, d, e = e, c, d
                    else (a, c, d, e) = (e, a, c, d);               //        else:      a, c, d, e = e, a, c, d
                    if (b < d)                                          //    if b < d:
                        if (b < e) (a, b, c, d, e) = (b, e, d, c, a);   //        if b < e:  return b, e, d, c, a
                        else (a, b, c, d, e) = (e, b, d, c, a);         //        else:      return e, b, d, c, a
                    else                                                //    else:
                        if (b < c) (a, b, c, d, e) = (e, d, b, c, a);   //        if b < c:  return e, d, b, c, a
                    else (a, b, c, d, e) = (e, d, c, b, a);         //        else:      return e, d, c, b, a
#pragma warning restore CS1717 // Assignment made to same variable
#pragma warning restore IDE0059 // Unnecessary assignment of a value

                    //!!if (b != arr[1] || c != arr[2] || d != arr[3] || e != arr[4])
                    //!!    CWriteLine("sort error");

                    // ignore 0 value items
                    ushort r;
                    if (d == 0) r = e;          // 4 or 5 0's, result is e, which may or may not itself be 0
                    else if (b == 0) r = d;     // 2 or 3 zeros (for 3 zeros d or e or (d+e)/2 will all do
                    else r = c;                 // 0 or 1 zeros (for 1 zero  c or d of (c+d)/2 will all do

                    oa[i] = r;
                }
            }

            int end = (height - 1) * width;
            for (int x = 0; x < width; x++) {
                oa[x] = ia[x];
                oa[x + end] = ia[x + end];
            }
            end = width - 1;
            for (int y = 0; y < height; y++) {
                oa[y * width] = ia[y * width];
                oa[y * width + end] = ia[y * width + end];
            }

            //long et = System.Diagnostics.Stopwatch.GetTimestamp();
            //ttime += et - st;
            //mdone++;
            //if (mdone == 100) {
            //    double fr = System.Diagnostics.Stopwatch.Frequency;
            //    double at = ttime / mdone / fr * 1e3;
            //    CWriteLine("avg median time ms=" + at);
            //    mdone = 0; // ttime = 0;
            //}
        }

        // Sort5 hasbeen moved inline
        //static (ushort a, ushort b, ushort c, ushort d, ushort e) Sort5(ushort a, ushort b, ushort c, ushort d, ushort e) {
        //    // Sort 5 values with 7 Comparisons'
        //    // https://stackoverflow.com/questions/1534748/design-an-efficient-algorithm-to-sort-5-distinct-keys-in-fewer-than-8-comparison
        //    // converted from the Python
        //    if (a < b) (a, b) = (b, a);                //    if a < b:      a, b = b, a
        //    if (c < d) (c, d) = (d, c);                //    if c < d:      c, d = d, c
        //    if (a < c) (a, b, c, d) = (c, d, a, b);    //    if a < c:      a, b, c, d = c, d, a, b
        //    if (e < c)                                 //    if e < c:
        //        if (e < d) { } // pass                 //        if e < d:  pass
        //        else (d, e) = (e, d);                  //        else:      d, e = e, d
        //    else                                       //    else:
        //        if (e < a) (c, d, e) = (e, c, d);      //        if e < a:  c, d, e = e, c, d
        //        else (a, c, d, e) = (e, a, c, d);      //        else:      a, c, d, e = e, a, c, d
        //    if (b < d)                                 //    if b < d:
        //        if (b < e) return (b, e, d, c, a);     //        if b < e:  return b, e, d, c, a
        //        else return (e, b, d, c, a);           //        else:      return e, b, d, c, a
        //    else                                       //    else:
        //        if (b < c) return (e, d, b, c, a);     //        if b < c:  return e, d, b, c, a
        //        else return (e, d, c, b, a);           //        else:      return e, d, c, b, a
        //}


        static void RawAllocate() {
            rsize = width * height;
            if (DepthArr != null && rsize == DepthArr.Length) return;  // already done
            CWriteLine($"RawAllocate {width}x{height}=>{rsize}");
            ColorData = new byte[colWidth * colHeight * colBytesPerPixel];
            DepthArr = new ushort[rsize];      // 512 x 424
            InfraredArr = new ushort[rsize];      // 512 x 424
            RGBArr = new UInt32[rsize];           // 512 x 424
            DepthArrUnFilt = new ushort[rsize];      // 512 x 424
            DepthArrFloat = new float[rsize];      // 512 x 424
            TempByteArray = new byte[rsize * 4];      // 512 x 424
            DepthArrEdge = new byte[rsize];      // 512 x 424
            DepthArr4 = new byte[rsize * 4];      // 512 x 424 * 4 rgba (b and a not used, a added for three removal of RGBFormat)
            DepthChangeArr = new byte[rsize * 3];      // 512 x 424, rgb
            IndexArr = new byte[rsize];      // 512 x 424
            InfraredArrFloat = new float[rsize];      // 512 x 424
            DepthArrOld = new ushort[rsize];      // 512 x 424
            DepthArrBack = new float[rsize];      // 512 x 424
            DepthArrBad = new float[rsize];      // 512 x 424
            LoadBackground();   // establish old background at start if possible
            DepthArrGeom = new float[1];      // allocated on demand
        }

        // https://stackoverflow.com/questions/480960/how-do-i-calculate-the-median-of-five-in-c
        // this is interesting but not used at least for now, because of our special case need for handling 0 (unknown) values

        //    static ushort MedianOfFive(ushort a, ushort b, ushort c, ushort d, ushort e) {
        //        return b < a ? d < c ? b < d ? a < e ? a < d ? e < d ? e : d
        //                                                     : c < a ? c : a
        //                                             : e < d ? a < d ? a : d
        //                                                     : c < e ? c : e
        //                                     : c < e ? b < c ? a < c ? a : c
        //                                                     : e < b ? e : b
        //                                             : b < e ? a < e ? a : e
        //                                                     : c < b ? c : b
        //                             : b < c ? a < e ? a < c ? e < c ? e : c
        //                                                     : d < a ? d : a
        //                                             : e < c ? a < c ? a : c
        //                                                     : d < e ? d : e
        //                                     : d < e ? b < d ? a < d ? a : d
        //                                                     : e < b ? e : b
        //                                             : b < e ? a < e ? a : e
        //                                                     : d < b ? d : b
        //                     : d < c ? a < d ? b < e ? b < d ? e < d ? e : d
        //                                                     : c < b ? c : b
        //                                             : e < d ? b < d ? b : d
        //                                                     : c < e ? c : e
        //                                     : c < e ? a < c ? b < c ? b : c
        //                                                     : e < a ? e : a
        //                                             : a < e ? b < e ? b : e
        //                                                     : c < a ? c : a
        //                             : a < c ? b < e ? b < c ? e < c ? e : c
        //                                                     : d < b ? d : b
        //                                             : e < c ? b < c ? b : c
        //                                                     : d < e ? d : e
        //                                     : d < e ? a < d ? b < d ? b : d
        //                                                     : e < a ? e : a
        //                                             : a < e ? b < e ? b : e
        //                                                     : d < a ? d : a;
        //    }


    }  // class Program
}

/**
 * note, 
 * 
 * Kinect 2: Microsoft.Kinect.dll, Microsoft.Kinect.xml (32 and 64 bit available)
 * Kinect 4 Azure, k4a, depthengine_2_0.dll, Microsoft.Azure.Kinect.Sensor.dll (32 and 64 bit available)
 * stereolabs.zed.dll, stereolabs.zed.xml, sl_zed_c.dll for ZED *64 b it only?)
 * 
 * */
