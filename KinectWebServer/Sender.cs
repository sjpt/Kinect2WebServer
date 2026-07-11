// #pra gma warning disable IDE0040, IDE0049, CA1805, CA1304, CA1305, IDE0008, IDE0055, IDE0058, IDE0048, IDE0011

using System;
using System.Collections.Generic;
using Fleck;
using System.Text.Json;
using System.Numerics;


//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

namespace Kinect2WebServer {
    partial class Program {
        // WebSockets changed to use Fleck, with code from Kinect1  https://github.com/LightBuzz/Kinect-HTML5
        static readonly List<IWebSocketConnection> _clients = new();

        static readonly JsonSerializerOptions jsonOptions = new() { IncludeFields = true };

        private static void InitializeConnection() {
            var server = new WebSocketServer("ws://127.0.0.1:8181");

            server.Start(socket => {
                socket.OnOpen = () => {
                    lock (_locker) {
                        _clients.Add(socket);
                        CWriteLine("new client, num=" + _clients.Count);
                        if (_clients.Count == 1) {
                            var openok = Open();
                            if (!openok) {
                                CWriteLine("cannot open default KinectSensor");
                                return;
                            }
                            Azure._lastAzuremsfr = "newconnection";
                            Newmsfr(null);
                        }
                        SendKinsize(socket);
                    }
                };

                socket.OnClose = () => {
                    _clients.Remove(socket);
                    CWriteLine("closed client, left=" + _clients.Count);
                    if (_clients.Count == 0) {
                        CWriteLine("closing Kinect", ConsoleColor.DarkYellow);
                        Close();
                    }
                };

                socket.OnError = e => {
                    CWriteLine("error in client: " + e.Message + "left=" + _clients.Count);
                    socket.OnClose();
                };

                socket.OnMessage = (mess) => DoMessage(mess, socket);
            });
        }

        protected static void Send(IWebSocketConnection socket, params string[] tag) {
            Send(socket, String.Join(' ', tag));
        }
        protected static void Send(IWebSocketConnection socket, string tag) {
            if (socket != null)
                socket.Send(tag + "  <" + Now() + " " + lastMessage);
        }

        static byte[] Bigbin = new byte[1000];

        protected static void Send(IWebSocketConnection socket, float[] data, int len = -1) {
            int s = (len == -1 ? data.Length : len) * 4;
            if (Bigbin.Length < s) Bigbin = new byte[s];
            Buffer.BlockCopy(data, 0, Bigbin, 0, s);
            if (socket != null) socket.Send(Bigbin, s);
        }

        /** keep some arrays so at least we don't have to keep creating them */
        // static readonly Dictionary<int, float[]> floatArrays = new();
        // no, just keep one big one
        static float[] SendFloatArray = Array.Empty<float>();
        // not used any more static readonly Dictionary<int, byte[]> byteArrays = new();
        /** horrible copy/Marshall and send of VecPlus[] */
        protected static void Send(IWebSocketConnection socket, string idstring, VecPlus[] vp) {
            int l = vp.Length;
            if (l * 4 > SendFloatArray.Length) SendFloatArray = new float[l * 4];
            float[] fl = SendFloatArray;
            int o = 0;
            for (int i = 0; i < l; i++) {
                VecPlus r = vp[i];
                fl[o++] = r.pos.X;
                fl[o++] = r.pos.Y;
                fl[o++] = r.pos.Z;
                fl[o++] = r.v;
            }
            Send(socket, idstring);
            Send(socket, fl, l * 4);
        }

        /** horrible copy/Marshall and send of Vector3[] */
        protected static void Send(IWebSocketConnection socket, string idstring, Vector3[] vp) {
            int l = vp.Length;
            if (l * 3 > SendFloatArray.Length) SendFloatArray = new float[l * 3];
            float[] fl = SendFloatArray;
            int o = 0;
            for (int i = 0; i < l; i++) {
                Vector3 r = vp[i];
                fl[o++] = r.X;
                fl[o++] = r.Y;
                fl[o++] = r.Z;
            }
            Send(socket, idstring);
            Send(socket, fl, l * 3);
        }

        static readonly object _locker = new();

        /** handle request message from client */
        static void DoMessage(string messages, IWebSocketConnection socket) {
           lock(_locker) {
                stagelist = new();
                Stage("#stages");
                Stage("domessage " + messages);
                string[] ss = messages.Split("!!");
                foreach (string message in ss)
                    oneMessage(message);

                StageEnd("domessage " + messages);
                if (socket != null) Send(socket, String.Join(", ", stagelist)); else CWriteLine(String.Join("\n", stagelist));

                void oneMessage(string message) {
                    try {
                        message = message.ToLower().Trim();
                        var args = message.Split(' ');
                        int alen = args.Length;
                        if (alen == 0) return;
                        string argv = "";
                        bool hasarg = false;
                        if (args.Length > 1) {
                            argv = args[alen - 1]; // cheat way to skip options =, eg in 'xx = 5'
                            hasarg = true;
                        }
                        string name = args[0].ToLower();

                        // query a variable, and if a new value given set it
                        // would like to have made this more generic, but can't quite see how
                        void qseti(ref int value) {
                            string r = $"{name} = {value}";
                            if (hasarg) r += $" => {value = int.Parse(argv)}";
                            CWriteLine(r); if (socket != null) socket.Send("= " + r);
                        }
                        void qsetus(ref ushort value) {
                            string r = $"{name} = {value}";
                            if (hasarg) r += $" => {value = ushort.Parse(argv)}";
                            CWriteLine(r); if (socket != null) socket.Send("= " + r);
                        }
                        void qsetf(ref float value) {
                            string r = $"{name} = {value}";
                            if (hasarg) r += $" => {value = float.Parse(argv)}";
                            CWriteLine(r); if (socket != null) socket.Send("= " + r);
                        }
                        void qsetb(ref bool value) {
                            string r = $"{name} = {value}";
                            if (hasarg) r += $" => {value = bool.Parse(argv)}";
                            CWriteLine(r); if (socket != null) socket.Send("= " + r);
                        }
                        void qsetmm(ref Matrix4x4 value) {
                            string r = $"{name} = \n{value}";
                            if (hasarg) {
                                if (args.Length == 17) {
                                    float[] e = new float[16]; for (int i = 0; i < 16; i++) e[i] = float.Parse(args[i + 1]);
                                    value = new Matrix4x4(e[0], e[1], e[2], e[3], e[4], e[5], e[6], e[7], e[8], e[9], e[10], e[11], e[12], e[13], e[14], e[15]);
                                    r += $" =>\n{value}";
                                } else {
                                    r += $" unchanged, args length {args.Length} != 17";
                                }
                            }
                            CWriteLine(r); if (socket != null) socket.Send("= " + r);
                        }

                        // valops are shared with client, action getsv will transfer them all to client
                        var valops = new Dictionary<String, Action> {
                        { "maxdepth", () => { qseti(ref maxDepth); } },
                        { "maxd", () => { qsetus(ref maxd); } },
                        { "gmaxd", () => { qsetus(ref gmaxd); } },
                        { "tad4background", () => { qsetb(ref tad4background); } },
                        { "filterback", () => { qsetb(ref filterback); } },
                        { "backdamp", () => { qsetf(ref backDamp); } },
                        { "backextra", () => { qsetf(ref backExtra); } },
                        { "fixdepth", () => { qsetus(ref fixDepth); } },
                        { "backmin", () => { qsetf(ref backMin); } },
                        { "baddamp", () => { qsetf(ref baddamp); } },
                        // { "irthresh", () => { if (i sK2) qseti(ref irthresh2); else qseti(ref irthresh4); } },
                        { "changedamp", () => { qsetf(ref changeDamp); } },
                        // {"twistdamp", () => { qsetf(ref twistDamp); } },
                        { "minchange", () => { qsetf(ref minChange); } },
                        // { "corestep", () => { int o = coreStep; qseti(ref coreStep); if (o != coreStep) Allocate(socket); } },
                        { "maxchange", () => { qsetf(ref maxChange); } },
                        { "corewidthtarget", () => { int o = coreWidthTarget; qseti(ref coreWidthTarget); if (o != coreWidthTarget) Allocate(socket); } },
                        { "edgethresh", () => { qseti(ref edgeThresh); } },
                        { "depthchangethresh", () => { qseti(ref depthChangeThresh); } },
                        { "mingoodn", () => { qsetf(ref minGoodn); } },
                        { "oldtwist", () => { qsetb(ref oldtwist); } },
                        { "depthgeomstep", () => { qseti(ref depthGeomStep); } },
                        { "domedian", () => { qsetb(ref doMedian); } },
                        { "flowreport", () => { qsetb(ref flowReport); } },
                        { "mindepthextra", () => { qsetf(ref minDepthExtra); } },
                        { "corefilterposweight", () => { qsetf(ref coreFilterPosWeight); } },
                        { "corefilterchangeweight", () => { qsetf(ref coreFilterChangeWeight); } },
                        { "depthgeomfilter", () => { qsetb(ref depthGeomFilter); } },
                        { "usegeometrygrid", () => { qsetb(ref UseGeometryGrid); } },
                        { "camviewrange", () => { qsetf(ref camviewRange); } },
                        { "maxedgegeompoints", () => { qseti(ref maxedgeGeomPoints); } },
                        { "lastedgegeompoints", () => { qseti(ref lastedgeGeomPoints); } },
                        { "coresmooth", () => { qsetf(ref coresmooth); } },
                        { "cofsmooth", () => { qsetf(ref cofsmooth); } },
                        { "coretraildamp", () => { qsetf(ref coretraildamp); } },
                        { "corexx", () => { qseti(ref corexx); CoreAllocate(); } },
                        { "eigennormals", () => { qsetb(ref eigenNormals); CoreAllocate(); } },
                    };
                        var getsv = () => {
                            foreach (KeyValuePair<string, Action> pair in valops) {
                                oneMessage(pair.Key);
                            }
                            CWriteLine("#endgetsv");
                            Send(socket, "#endgetsv");
                            Stage("#endgetsv");
                        };

                        // actops are 'pure' actions, not used for initialization (?)
                        var actops = new Dictionary<String, Action> {
                        { "cammat", () => { qsetmm(ref camMat); } },
                        { "kincammat", () => { qsetmm(ref kinCamMat); } },
                        { "kincammatu", () => { kinCamMat = new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1); } },
                        { "savedir", () => { _savedir = args[1]; } },
                        { "checkcalibration", () => { checkCalibration(0,0,1000); } },
                        { "segment", () => { segmentnum = -1; Segment(); } },
                        { "field3d", () => { Field3(); } },
                        { "resetbackground", () => { ResetBackground(); } },
                        { "killbackgroundbad", () => { KillBackgroundBad(); } },
                        { "movebackground", () => { MoveBackground(float.Parse(args[1]), float.Parse(args[2])); } },
                        { "movelikelybackground", () => { MoveLikelyBackground(float.Parse(args[1])); } },
                        { "savebackground", () => { SaveBackground(); } },
                        { "loadbackground", () => { LoadBackground(); } },
                        { "debug", () => { qsetb(ref _debug); } },
                        { "info", () => { Info(socket); } },
                        { "killothersockets", () => { KillOtherSockets(socket); } },
                        { "useazurecalibration", () => { qsetb(ref Azure.UseAzureCalibration); } },
                        //{ "backgroundstats", () => {       // background stats for given pixel
                        //    string d = JsonSerializer.Serialize(backgroundStats[int.Parse(argv)], jsonOptions);
                        //    CWriteLine(d);
                        //    if (socket != null) { Send(socket, "backgroundStats"); socket.Send(d); }
                        //} },
                        { "newmsfr", () => { Newmsfr(socket, message); } },
                        { "close", () => { Close(); } },
                        { "open", () => { Open(); } },
                        { "kbest", () => { Close(); if (bestAzure) {socket.Send("kbest k4"); azure.Init(message, socket); } else {socket.Send("kbest k2"); kinect2.Init(message, socket);} } },
                        { "k2", () => { Close(); kinect2.Init(message, socket); } },
                        { "k4", () => { Close(); azure.Init(message, socket); } },
                        { "zed", () => { Close(); zed.Init(message, socket); } },  // commented out Stephen 21 Dev 2023, readded 15 June 2024
                        // { "azure", () => { AzureInit(message, socket); } },
                        { "get", () => { AcquireSendData(socket, message); } },
                        // { "eigen", () => { Eigen.Eigentest(); } },
                        { "", () => { CWriteLine(""); } },
                        { "cls", () => { Console.Clear(); } },
                        { "getsv", () => { getsv(); } },
                        { "?accel", () => { 
                            Vector3 v = activeDevice.Accel();
                            Send(socket, $"?accel {v.X} {v.Y} {v.Z}");
                        }},
                        { "?", () => { getsv(); } }
                    };
                        valops.TryGetValue(name, out Action act);
                        if (act == null) actops.TryGetValue(name, out act);
                        if (act == null) {
                            CWriteLine("message ignored '" + message + "'");
                        } else {
                            act();
                        }
                    }
                    catch (Exception e) {
                        CWriteLine("error handling message " + message + " " + e);
                    }
                } // lock
            }   // oneMessage
        }   // doMessage

        static void KillOtherSockets(IWebSocketConnection socket) {
            for (int i = _clients.Count - 1; i >= 0; i--) {
                var s = _clients[i];
                if (s != socket) {
                    s.Send("#forceclose: ");
                    s.Close();
                }
            }
        }


        /** send depth summary data to requesting socket. Could be optimized if there were multiple sockets working at once. */
        private static void SendDepthBlock(IWebSocketConnection socket, VecPlus[] adata, string usage) {
            Send(socket, usage, adata); // OutCorePosition); //  ;
        }

        static bool depthGeomFilter = false;
        /** send depthGeometry when requested, in geometry not raw format, NOT related to any coreXXX */
        private static void SendDepthGeometry(IWebSocketConnection socket) {
            int start = depthGeomStep / 2;
            int nx = (width + depthGeomStep - start - 1) / depthGeomStep;
            int ny = (height + depthGeomStep - start - 1) / depthGeomStep;
            int size = nx * ny;
            if (DepthArrGeom.Length != size * 3)
                DepthArrGeom = new float[size * 3];
            var i = 0;
            for (int yy = start; yy < height; yy += depthGeomStep) {
                for (int xx = start; xx < width; xx += depthGeomStep) {
                    var zz = DepthArr[xx + yy * width];
                    Vector3 g = ToGeometry(xx, yy, zz);
                    if (zz == 0 || 
                        (filterback && zz >= DepthArrBack[xx + yy * width] * backExtra) || zz > maxDepth )
                        g.Z = Single.NaN;
                    // find the corresponding core slot and hence corePosition
                    int co;
                    if (depthGeomFilter) {
                        if (UseGeometryGrid) {
                            if (!ToGridBucket(g, out int cx, out int cy, out co)) continue;
                        } else {
                            int cx = (int)Math.Min(xx / coreStepx, coreWidth - 1);  // ?????????? to confirm ???????????
                            int cy = (int)Math.Min(yy / coreStepy, coreHeight - 1);
                            co = cx + cy * coreWidth;
                        }
                        VecPlus cv = CorePosition[co];
                        if (cv.v == 0) continue;
                        if (g.Z - cv.pos.Z > 0.1) continue;
                        // if (Single.IsNaN(CorePosition[cx + cy*coreWidth].pos.X)) continue;
                    }
            
                    DepthArrGeom[i++] = g.X;
                    DepthArrGeom[i++] = g.Y;
                    DepthArrGeom[i++] = g.Z;
                }
            }
            if (i > DepthArrGeom.Length)
                Console.WriteLine("unexpected lengths in SendDepthGeometry");
            Send(socket, "depthgeometry", nx + "", ny + "", depthGeomStep + "");
            Send(socket, DepthArrGeom, i);
        }

        /** send the Kinect data for skeleton */
        static void SendSkeleton(IWebSocketConnection socket) {
            var bodyData = Kinect2.SerialiseBodyData(Kinect2.bodysecs);
            if (bodyData != null) {
                Send(socket, "bodydata");
                socket.Send(bodyData);
            }
        }

        /** send the index data to the client */
        private static void SendIndex(IWebSocketConnection socket) {
            Send(socket, "indexdata");
            socket.Send(IndexArr);
        }

        /** look for changes at the Kinect pixel level, could be extended to understand depthGeomStep  */
        private static void SendDepthChange(IWebSocketConnection socket) {
            byte[] aout = DepthChangeArr;
            byte r, g, b;   // output r,g,b
            int op = 0;     // output position
            for (int i = 0; i < rsize; i++) {
                ushort n = DepthArr[i];
                ushort o = DepthArrOld[i];
                if (n == 0 || o == 0) {
                    r = g = b = 255;
                } else {
                    ushort bk = (ushort)(DepthArrBack[i] * backExtra);
                    if (n > bk) n = bk;
                    if (o > bk) o = bk;
                    if (n - o > depthChangeThresh) { r = 255; g = b = 0; } else if (o - n > depthChangeThresh) { g = 255; r = b = 0; } else if (o > n) { r = 0; b = 128; g = (byte)(255 * (o - n) / depthChangeThresh); } else { g = 0; b = 128; r = (byte)(255 * (n - o) / depthChangeThresh); }
                    //else { r = g = 160; b = (byte)(255 / depthChangeThresh / 2 * (o - n + depthChangeThresh)); }
                }
                aout[op++] = r; aout[op++] = g; aout[op++] = b;
            }
            Send(socket, "depthchange");
            socket.Send(DepthChangeArr);
        }


        /** send raw depth when requested, Kinect depth data in raw format, but converted to float range 0..1 */
        private static void SendDepthRaw(IWebSocketConnection socket) {
            float k = 0.75f / maxDepth;
            int l = DepthArr.Length;

            for (int i = 0; i < l; i++) {
                float v = DepthArr[i] * k;
                DepthArrFloat[i] = v*v;
                // DepthArrFloat[i] = v > 1 ? 0 : v;
                // debug DepthArrFloat[i] = v > 1 ? 1 : 0;
            }

            Send(socket, "depthraw");
            Send(socket, DepthArrFloat);
        }


        /** send raw depth when requested, Kinect depth data in raw format, converted to float range 0..1 */
        private static void SendDepthNoback(IWebSocketConnection socket) {
            float k = 0.75f / maxDepth;
            int _irthresh = 0; // Irthresh();
            for (int i = 0; i < DepthArr.Length; i++) {
                var d = (float)DepthArr[i];
                var ir = InfraredArr[i];
                var db = DepthArrBack[i] * backExtra;
                DepthArrFloat[i] = ((d > db && db != 0) || ir < _irthresh) ? 0 : (d * k) * (d * k);
            }
            Send(socket, "depthnoback");
            Send(socket, DepthArrFloat);
        }

        /** send background when requested, Kinect depth data in raw format, converted to float range 0..1, */
        private static void SendDepthBack(IWebSocketConnection socket) {
            float k = 0.75f / maxDepth;
            for (int i = 0; i < DepthArr.Length; i++) {
                float d = DepthArrBack[i];
                DepthArrFloat[i] = (d * k) * (d * k); // ??? 5
            }
            Send(socket, "depthback");
            Send(socket, DepthArrFloat);
        }

        static ushort Nobackv(int i) {
            ushort v = DepthArr[i];
            float bbc = DepthArrBack[i] * backExtra;
            if (bbc != 0 && v > bbc) v = 0;
            return v;
        }

        private static int // edgeGeomPoints = 0,  // max number of points to send to client
            maxedgeGeomPoints = 0,              // max number of potential edge points seen since reset
            lastedgeGeomPoints = 0;             // last number of potential edge points
        private static float[] edgeGeomPointsArr = { };
        private static int decall = -999;
        /** compute and send raw level edge information  */
        private static void ComputeDepthEdge() {
            if (callnum == decall) return;
            if (edgeGeomPointsArr.Length < maxedgeGeomPoints * 3) // reallocate with 20% extra
                edgeGeomPointsArr = new float[3 * (int)(maxedgeGeomPoints * 1.2)];
            Array.Fill(edgeGeomPointsArr, float.NaN);

            decall = callnum;
            // ushort[] ia = DepthArr;
            int pi = 0; // index into edgeGeom
            int n = 0;
            for (int y = 1; y < height - 1; y++) {
                for (int x = 1; x < width - 1; x++) {
                    int i = x + y * width;

                    ushort cc = Nobackv(i);
                    if (cc == 0) { DepthArrEdge[i] = 0; continue; }
                    //if (cc > bbc) cc = 0;
                    ushort a = cc;
                    ushort b = Nobackv(i + width);
                    ushort c = Nobackv(i - width);
                    ushort d = Nobackv(i + 1);
                    ushort e = Nobackv(i - 1);

                    //byte v = 0;
                    //if (b - a > edgeThresh) v |= 32;
                    //if (a - c > edgeThresh) v |= 32;
                    //if (a - b > edgeThresh) v |= 16;
                    //if (c - a > edgeThresh) v |= 16;

                    //if (d - a > edgeThresh) v |= 64;
                    //if (a - e > edgeThresh) v |= 64;
                    //if (a - d > edgeThresh) v |= 128;
                    //if (e - a > edgeThresh) v |= 128;
                    //if (v == (32 | 16 | 64 | 128)) v = 0; // remove isolated points (noise)

                    byte v = 0;
                    // var abs = Math.Abs<ushort>;
                    const int bad = 0;
                    if (b != bad && Math.Abs(b - a) > edgeThresh) v++;
                    if (c != bad && Math.Abs(c - a) > edgeThresh) v++;
                    if (d != bad && Math.Abs(d - a) > edgeThresh) v++;
                    if (e != bad && Math.Abs(e - a) > edgeThresh) v++;


                    if (v >= 3) v = 0; // remove isolated points (noise)

                    DepthArrEdge[i] = (byte) (v*60);  // largest v is 4, so this maps to 0..240 for convenient use as byte graphics property

                    if (v != 0) {
                        n++;
                        if (pi < edgeGeomPointsArr.Length) {
                            Vector3 g = ToGeometry(x, y, cc);
                            edgeGeomPointsArr[pi++] = g.X;
                            edgeGeomPointsArr[pi++] = g.Y;
                            edgeGeomPointsArr[pi++] = g.Z;
                        }
                    }

                    //bool edge = Math.Abs(b - a) > edgeThresh || Math.Abs(c - a) > edgeThresh || Math.Abs(d - a) > edgeThresh || Math.Abs(e - a) > edgeThresh;
                    //DepthArrEdge[i] = edge ? (byte)255 : (byte)0;
                }   
            }
            lastedgeGeomPoints = n;
            maxedgeGeomPoints = Math.Max(lastedgeGeomPoints, maxedgeGeomPoints); // will automatically set #points enough, 1 step behind
        }
        private static void SendDepthEdge(IWebSocketConnection socket) {
            ComputeDepthEdge();
            Send(socket, "depthedge");
            socket.Send(DepthArrEdge);
        }

        private static void SendEdgeGeom(IWebSocketConnection socket) {
            ComputeDepthEdge();
            Send(socket, "edgegeom");
            Send(socket, edgeGeomPointsArr, Math.Min(lastedgeGeomPoints * 3, edgeGeomPointsArr.Length));
        }
        /** send red/green of current/previous frame, or other for test */
        static double maxd4 = 6300; // max depth recorded
        static int stripewidth = 100;
        private static void SendDepth4(IWebSocketConnection socket) {
            maxd4 = maxDepth; /* 6300; */ stripewidth = 100;
            int i = 0, o = 0;
            for (int y = 1; y < height - 1; y++) {
                for (int x = 1; x < width - 1; x++) {
                    double r = 0, g = 0, b = 1;
                    ushort d = DepthArr[i];
                    if (d != 0) {
                        r = 1 - Math.Pow(dd(d), 1);
                        double v = d % stripewidth, s = v < stripewidth / 2 ? v : stripewidth - v;             // sawtooth, reduce noise at 0..1 edge
                        g = s / (stripewidth / 2);     // dd(DepthArrOld[i]); // to see change
                        b = (d > maxDepth) ? 1 : 0;
                        if (d % 1000 < 20 || d % 1000 > 1000-20) b = 1;  // to see metres
                    }

                    DepthArr4[o++] = (byte)(r * 255);
                    DepthArr4[o++] = (byte)(g * 255);
                    DepthArr4[o++] = (byte)(b * 255);
                    DepthArr4[o++] = 255;
                    i++;
                }
            }
            Send(socket, "depth4");
            socket.Send(DepthArr4);

            double dd(ushort v) {
                if (v > maxd4/2)
                    v = (ushort)(v +0); // debug
                double r =
                    (v == 0) ? 0 :
                    (v < 500) ? 1 :
                    (v > maxd4) ? 0 :
                        ((maxd4 - v) / (maxd4 - 500.0));
                return r;
                } // dd
        }


        /** send the infrared data to the client */
        private static void SendInfrared(IWebSocketConnection socket) {
            // copy as float, not worked out how to use ushort,
            float k = (float)(1.0 / 1000);
            for (var i = 0; i < InfraredArr.Length; i++) {
                // float d = DepthArr[i];
                InfraredArrFloat[i] = (float)Math.Sqrt(InfraredArr[i] * k); //  * d/1000 * d / 1000);
            }

            Send(socket, "infrared");
            Send(socket, InfraredArrFloat);
        }




        // static int mdone = 0;   // debug giming for median filter
        // static long ttime = 0;

        // private static int lastCount = 999;  // ensure we report transition to no skeletons ... no always report empty lsit

    }  // class Program
}
