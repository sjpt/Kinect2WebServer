// #pra gma warning disable IDE0040, IDE0049, CA1805, CA1304, CA1305, IDE0008, IDE0055, IDE0058, IDE0048, IDE0011

using Fleck;
using MathNet.Numerics.Statistics;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;


//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

namespace Kinect2WebServer {
    // partial class MProgram {
    partial class Program {
        static readonly object _locker = new();

        private static Dictionary<String, Action> valops;
        private static Dictionary<String, Action> actops;
        private static string[] args;
        private static int alen;
        private static string argv;
        private static bool hasarg;
        private static string name;
        private static string messageSL;

        private static IWebSocketConnection socketSL;


        // query a variable, and if a new value given set it
        // would like to have made this more generic, but can't quite see how
        static void qseti(ref int value) {
            string r = $"{name} = {value}";
            if (hasarg) r += $" => {value = int.Parse(argv)}";
            CWriteLine(r); if (socketSL != null) socketSL.Send("= " + r);
        }
        static void qsetus(ref ushort value) {
            string r = $"{name} = {value}";
            if (hasarg) r += $" => {value = ushort.Parse(argv)}";
            CWriteLine(r); if (socketSL != null) socketSL.Send("= " + r);
        }
        static void qsetf(ref float value) {
            string r = $"{name} = {value}";
            if (hasarg) r += $" => {value = float.Parse(argv)}";
            CWriteLine(r); if (socketSL != null) socketSL.Send("= " + r);
        }
        static void qsetb(ref bool value) {
            string r = $"{name} = {value}";
            if (hasarg) r += $" => {value = bool.Parse(argv)}";
            CWriteLine(r); if (socketSL != null) socketSL.Send("= " + r);
        }
        static void qsetmm(ref Matrix4x4 value) {
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
            CWriteLine(r); if (socketSL != null) socketSL.Send("= " + r);
        }

        private static void getsv() {
            foreach (KeyValuePair<string, Action> pair in valops) {
                oneMessage(pair.Key);
            }
            CWriteLine("#endgetsv");
            Send(socketSL, "#endgetsv");
            Stage("#endgetsv");
        }
        static double lastgct = 0;
        static long lastgcbytes = 0;

        static void makeops() { 
        // actops are 'pure' actions, not used for initialization (?)
        actops = new Dictionary<String, Action> {
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
            { "info", () => { Info(socketSL); } },
            { "killothersockets", () => { KillOtherSockets(socketSL); } },
            { "useazurecalibration", () => { qsetb(ref Azure.UseAzureCalibration); } },
            //{ "backgroundstats", () => {       // background stats for given pixel
            //    string d = JsonSerializer.Serialize(backgroundStats[int.Parse(argv)], jsonOptions);
            //    CWriteLine(d);
            //    if (socketSL != null) { Send(socketSL, "backgroundStats"); socketSL.Send(d); }
            //} },
            { "newmsfr", () => { Newmsfr(socketSL, messageSL); } },
            { "close", () => { Close(); } },
            { "open", () => { Open(); } },
            { "kbest", () => { Close(); if (bestAzure) {socketSL.Send("kbest k4"); azure.Init(messageSL, socketSL); } else {socketSL.Send("kbest k2"); kinect2.Init(messageSL, socketSL);} } },
            { "k2", () => { Close(); kinect2.Init(messageSL, socketSL); } },
            { "k4", () => { Close(); azure.Init(messageSL, socketSL); } },
            { "orbbec", () => { Close(); orbbec.Init(messageSL, socketSL); } },
            { "zed", () => { Close(); zed.Init(messageSL, socketSL); } },  // commented out Stephen 21 Dev 2023, readded 15 June 2024
            // { "azure", () => { AzureInit(messageSL, socketSL); } },
            { "get", () => { AcquireSendData(socketSL, messageSL); } },
            // { "eigen", () => { Eigen.Eigentest(); } },
            { "", () => { CWriteLine(""); } },
            { "cls", () => { Console.Clear(); } },
            { "getsv", () => { getsv(); } },
            { "pingaccel", () => { Vector3 v = azure.PingAccel(500); CWriteLine($"pingaccel {v.X} {v.Y} {v.Z} {v} "); } },
            { "?accel", () => { 
                Vector3 v = activeDevice.Accel();
                Send(socketSL, $"?accel {v.X} {v.Y} {v.Z}");
            }},
            { "gc", () => { 
                var before = GC.GetTotalAllocatedBytes();
                // the collection nere never seems to make any difference
                GC.Collect();
                // GC.WaitForFullGCComplete();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var after = GC.GetTotalAllocatedBytes();
                var aftert = Now();
                var delta = after-lastgcbytes;
                var deltat = aftert - lastgct;
                var deltapmin = delta / deltat * 60;
                // CWriteLine($"before={before:0,000,000,000}, after={after:0,000,000,000}, delta={before-after}");
                CWriteLine($"lastgcbytes={lastgcbytes:#,###,###,##0}, after={after:#,###,###,##0}," + 
                    $" delta={delta:#,###,##0}, deltat={deltat/1000:#,###,##0.000}sec deltaPerMinute={deltapmin:#,###,###,##0}");
                lastgcbytes = after;
                lastgct = aftert;

            } },
            { "drainloop", () => { azure.Drainloop(uint.Parse(args[1]));} },
            { "?", () => { getsv(); } },
            { "makeops", () => { makeops(); } },
            { "??", () => { CWriteLine(String.Join(", ", actops.Keys)); } }
        };

        // valops are shared with client, action getsv will transfer them all to client
        valops = new Dictionary<String, Action> {
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
                // { "corestep", () => { int o = coreStep; qseti(ref coreStep); if (o != coreStep) Allocate(socketSL); } },
                { "maxchange", () => { qsetf(ref maxChange); } },
                { "corewidthtarget", () => { int o = coreWidthTarget; qseti(ref coreWidthTarget); if (o != coreWidthTarget) Allocate(socketSL); } },
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
        } // makeops

        private static void oneMessage(string message) {
            try {
                message = message.ToLower().Trim();
                messageSL = message;
                args = message.Split(' ');
                alen = args.Length;
                if (alen == 0) return;
                argv = "";
                hasarg = false;
                if (args.Length > 1) {
                    argv = args[alen - 1]; // cheat way to skip options =, eg in 'xx = 5'
                    hasarg = true;
                }
                name = args[0].ToLower();

                valops.TryGetValue(name, out Action act);
                if (act == null) actops.TryGetValue(name, out act);
                if (act == null) {
                    CWriteLine("messageSL ignored '" + message + "'");
                } else {
                    act();
                }
            }
            catch (Exception e) {
                CWriteLine("error handling messageSL " + message + " " + e);
            }
    }   // oneMessage


        /** handle request messageSL from client */
        static void DoMessage(string messages, IWebSocketConnection socket) {
            if (valops == null || valops == null) makeops();
           lock(_locker) {
                stagelist = new();
                startstage.Clear();     // its keys include the whole message (with its timestamp): without this it grew by one entry per request, ~20MB/h (organicart-3hz)
                Stage("#stages");
                Stage("domessage " + messages);
                string[] ss = messages.Split("!!");
                socketSL = socket;
                foreach (string message in ss)
                    oneMessage(message);

                StageEnd("domessage " + messages);
                if (socketSL != null) Send(socket, String.Join(", ", stagelist)); else CWriteLine(String.Join("\n", stagelist));

            } // lock
        }   // doMessage
    }
}
