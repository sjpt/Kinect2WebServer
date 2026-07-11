using Fleck;
using System;
using Microsoft.Kinect; // for Kinect2
using System.Collections.Generic;   // for List<Body>
using System.Linq;  // for bodies.Where

#pragma warning disable IDE0011 // Add braces


#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Kinect2WebServer {
    internal class Kinect2 : Program, ICaptureDevice {
        private KinectSensor k2ks;
        private MultiSourceFrameReader k2msfr;

        private static Body[] bodies; public static double bodysecs;
        private static List<Body> trackedBodies = new();

        public string Type() { return "Kinect2"; }

        void ICaptureDevice.CloseI() {
            k2ks.Close();
        }

        private MultiSourceFrame k2frame;
        bool ICaptureDevice.GetCapture() {
            // acquire all the data, as quickly as possible
            if (k2msfr == null) {
                CWriteLine("Send data from Kinect 2 with no k2msfr");
                return false;
            }
            k2frame = k2msfr.AcquireLatestFrame();
            return k2frame != null;
        }

        bool ICaptureDevice.GetDepthData() {
            if (k2frame.DepthFrameReference == null) return false;

            TimeSpan ctime = k2frame.DepthFrameReference.RelativeTime;
            timeDelta = (float)((ctime - depthTime).TotalMilliseconds / 1000);
            depthTime = ctime;

            DepthFrame frame = k2frame.DepthFrameReference.AcquireFrame();
            if (frame == null) return false;

            (DepthArr, DepthArrOld) = (DepthArrOld, DepthArr); // swap depth buffers
                                                               // { var t = DepthArr; DepthArr = DepthArrOld; DepthArrOld = t; }  // swap depth buffers
            if (doMedian) {
                frame.CopyFrameDataToArray(DepthArrUnFilt);     // get the new raw data DepthArrUnFilt
                frame.Dispose();
                MedianFilter(DepthArrUnFilt, DepthArr);         // and filter it into DepthArr
            } else {
                frame.CopyFrameDataToArray(DepthArr);           // get the new raw data DepthArr
                frame.Dispose();
            }
            return true;
        }

        bool ICaptureDevice.GetInfrared() {
            if (k2frame.InfraredFrameReference == null) return false;

            InfraredFrame frame = k2frame.InfraredFrameReference.AcquireFrame();
            if (frame == null) return false;
            // var fd = ks.ColorFrameSource.FrameDescription;
            frame.CopyFrameDataToArray(InfraredArr);
            frame.Dispose();
            return true;
        }

        public bool Init(string message, IWebSocketConnection socket) {
            width = height = 0;
            try {
                if (k2ks == null) k2ks = KinectSensor.GetDefault();
                if (k2ks == null) { //  || !ks.IsAvailable) {
                    CWriteLine("cannot access default Kinect v2 Sensor");
                    return false;
                }
                CWriteLine("default Kinect v2 Sensor found OK"); // open deferred till client actually wants it

                /** show availability status and keep it updated */
                void ShowAvailable() {
                    if (k2ks == null) return;
                    string d = "KinectSensor " + (k2ks.IsAvailable ? "" : "NOT ") + "available ... " + DateTime.Now;
                    CWriteLine(d, k2ks.IsAvailable ? ConsoleColor.DarkGreen : ConsoleColor.DarkRed);
                    // Console.Title = d;
                }
                ShowAvailable();
                k2ks.IsAvailableChanged += (object sender, IsAvailableChangedEventArgs e) => ShowAvailable();

                FrameDescription fd = k2ks.ColorFrameSource.FrameDescription; //  ks.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Rgba);
                colWidth = fd.Width;
                colHeight = fd.Height;
                colBytesPerPixel = (int)fd.BytesPerPixel;
                uint frameSize = fd.BytesPerPixel * fd.LengthInPixels;

                width = k2ks.DepthFrameSource.FrameDescription.Width;
                height = k2ks.DepthFrameSource.FrameDescription.Height;

                fovx = 70.6; fovy = 60;

            } catch (Exception e) {
                CWriteLine("Kinect2 error: " + e.Message, ConsoleColor.Red);
                return false;
            }
            devid = "K2";
            activeDevice = this;
            Newmsfr(socket);
            bodies = new Body[k2ks.BodyFrameSource.BodyCount];    // bodies for Azure not yet supported, may give error
            bool isok = width != 0;
            OpenI();
            return isok;
        }

        public void NewmsfrI(IWebSocketConnection socket, string k = "depth infrared") {
            FrameSourceTypes fst = 0;
            if (k2msfr != null) k2msfr.Dispose();
            if (k.Contains("depth")) fst |= FrameSourceTypes.Depth;
            if (k.Contains("body")) fst |= FrameSourceTypes.Body;
            if (k.Contains("color")) fst |= FrameSourceTypes.Color;
            if (k.Contains("infrared")) fst |= FrameSourceTypes.Infrared;
            if (k.Contains("bodyindex")) fst |= FrameSourceTypes.BodyIndex;
            k2msfr = k2ks.OpenMultiSourceFrameReader(fst);
        }

        public bool OpenI() {
            try {
                k2ks.Open();
            } catch (Exception e) {
                CWriteLine("error opening Kinect 2: " + e.Message);
            }
            if (!k2ks.IsOpen) return false;
            NewmsfrI(null);
            return true;

        }

        bool ICaptureDevice.GetIndex() {
            if (k2frame.InfraredFrameReference == null) return false;

            InfraredFrame frame = k2frame.InfraredFrameReference.AcquireFrame();
            if (frame == null) return false;
            // var fd = ks.ColorFrameSource.FrameDescription;
            frame.CopyFrameDataToArray(InfraredArr);
            frame.Dispose();
            return true;
        }

        bool ICaptureDevice.GetSkeleton() {
            if (k2frame.BodyFrameReference == null) return false;
            BodyFrame bf = k2frame.BodyFrameReference.AcquireFrame();
            if (bf == null) return false;
            bodysecs = bf.RelativeTime.TotalSeconds;
            bf.GetAndRefreshBodyData(bodies);
            bf.Dispose();
            return true;
        }

        bool ICaptureDevice.GetRGB() {
            if (k2frame.ColorFrameReference == null) return false;

            ColorFrame frame = k2frame.ColorFrameReference.AcquireFrame();
            if (frame == null) return false;
            frame.CopyRawFrameDataToArray(ColorData);
            frame.Dispose();
            return true;
        }

        public static string SerialiseBodyData(double secs) {
            trackedBodies = bodies.Where(b => b.IsTracked).ToList();
            //int llastCount = lastCount;
            //lastCount = trackedBodies.Count;
            //if (lastCount < 1 && llastCount == 0)
            //    return null;

            string json = trackedBodies.Serialize(secs);
            return json;
            //#### below instead of above if we need dummy Kinect 2
            //Console.WriteLine("skeleton support removed");
            //return "skeleton support removed";
        }

    }
}
