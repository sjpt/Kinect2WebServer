#pragma warning disable IDE0040, IDE0049, CA1805, CA1304, CA1305, IDE0008, IDE0055, IDE0058, IDE0048, IDE0011

using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
using System.Numerics;
using IWebSocketConnection = Fleck.IWebSocketConnection;

namespace Kinect2WebServer {
    partial class Program {
        /** average position over entire view, weighted by change */
        static void ReduceSingle(IWebSocketConnection socket) {
            VecPlus r = Grid.ReduceBlock(0, coreWidth, 0, coreHeight);
            Send(socket, $"reduceSingle {r.pos.X} {r.pos.Y} {r.pos.Z} {r.v} ");
        }

        static void SendSegment(IWebSocketConnection socket) {
            Send(socket, "msg=numSegments " + numSegments);   // ??? only debug
            Send(socket, "segment");
            socket.Send(SegmentId);
        }

        private static int segmentnum;
        static readonly int[] counts = new int[256];
        //static readonly float[] cweight = new float[256];
        //static readonly float[] hits = new float[256];
        //static readonly float[] weight = new float[256];

        static Statsxyz[] segstats;
        // find stats for the segments
        static void Segment() {
            if (segmentnum == callnum) return; segmentnum = callnum;
            Stage("Segment");
            //SegmentIds = new byte[coreWidth * coreHeight];                // which segment this core point is in
            //BaseSegment = new byte[256];                                  // 'base' segment for each segment
            // set up initial identity BaseSegment
            for (int i = 0; i < BaseSegment.Length; i++) BaseSegment[i] = (byte)i;

            // segment the items based on contiguity, items with v < 1 left with id = 0
            byte nextid = 1;
            for (int cy = 0; cy < coreHeight; cy++) {
                for (int cx = 0; cx < coreWidth; cx++) {
                    int co = cx + cy * coreWidth;
                    Vector3 pos = CorePosition[co].pos; // short term, ignore pos
                    float v = CorePosition[co].v;
                    // float change = CoreChange[co]; // (float) Math.Fl oor(v); use Change which is undamped
                    float hitfrac = Fract(v);
                    // if (change < 10 || hitfrac < 0.02) { SegmentId[co] = 0; continue; }
                    if (hitfrac < 0.02) { SegmentId[co] = 0; continue; }

                    // ??? compare to X and Y changes ??? or have change 0.1  as tunable
                    byte above = 0, left = 0;
                    if (cy > 0) {
                        above = SegmentId[co - coreWidth];
                        if (Math.Abs(pos.Z - CorePosition[co - coreWidth].pos.Z) > 0.1) above = 0;
                    }

                    if (cx > 0) {
                        left = SegmentId[co - 1];
                        if (Math.Abs(pos.Z - CorePosition[co - 1].pos.Z) > 0.1) left = 0;
                    }

                    if (above == 0) {
                        SegmentId[co] = left == 0 ? nextid++ : left;
                    } else {
                        SegmentId[co] = above;
                        if (left != 0) {
                            BaseSegment[left] = BaseSegment[above]; // merge left and above
                        }
                    }
                }  // x
            } // y

            // closure of BaseSegment
            int maxq = 0;
            for (int i = 0; i < nextid; i++) {
                byte x = BaseSegment[i];
                int q = 0;
                while (x != BaseSegment[x]) {
                    x = BaseSegment[x];
                    q++;
                    if (q > 255) 
                        throw new Exception("'BaseSegment closure error");
                }
                BaseSegment[i] = x;
                maxq = Math.Max(q, maxq);
            }

            // merge the segments, and count number (pending) and weight for each
            Array.Fill(counts, 0);
            //Array.Fill(cweight, 0);
            //Array.Fill(hits, 0);
            //Array.Fill(weight, 0);
            for (int co = 0; co < coreHeight * coreWidth; co++) {
                byte si = SegmentId[co] = BaseSegment[SegmentId[co]];
                //cweight[si] += Change[i] * Hitf(CorePosition[i].v);
                //hits[si] += Hitf(CorePosition[i].v);
                //weight[si] += Change[i];
                counts[si]++;
            }
            if (counts[0] < 10)
                Debug("counts[0] < 10");

            // find the segments with most members
            Array.Sort(counts, BaseSegment, 1, 255);  // '(int a, int b) => b - a': won't take a function for comparison , nearly as silly as Java,
            Array.Reverse(counts, 1, 255);
            Array.Reverse(BaseSegment, 1, 255);

            // renumber them, throwing away isolated groups (??? 3 to become tuneable ???)
            byte[] rn = new byte[256];
            for (numSegments = 1; counts[numSegments] > 3; numSegments++)
                rn[BaseSegment[numSegments]] = (byte)numSegments;

            for (int co = 0; co < coreHeight * coreWidth; co++)
                SegmentId[co] = rn[SegmentId[co]];
            StageEnd("Segment");

        }  // Segment
        static void SendSegstats(IWebSocketConnection socket) {
            SegmentStats();
            float[] ss = new float[segstats.Length * 11];
            int o = 0;
            for (int i = 0; i < segstats.Length; i++) {
                Statsxyz s = segstats[i];
                ss[o++] = s.n;
                ss[o++] = s.sw;
                ss[o++] = s.sx;
                ss[o++] = s.sy;
                ss[o++] = s.sz;
                ss[o++] = s.sxx;
                ss[o++] = s.syy;
                ss[o++] = s.szz;
                ss[o++] = s.sxy;
                ss[o++] = s.syz;
                ss[o++] = s.szx;
            }

            Send(socket, "segstats");
            Send(socket, ss);
        }

        static void SegmentStats() {
            //double[] n() { return new double[numSegments];  }
            //double[] w=n(), sx = n(), sy = n(), sz = n(), sxx = n(), syy = n(), szz = n(), sxy = n(), syz = n(), zzx = n();
            Segment();  // may already be done
            Stage("SegementStats");
            segstats = new Statsxyz[numSegments];
            for (int i = 0; i < CorePosition.Length; i++) {
                int seg = SegmentId[i];
                ref Statsxyz s = ref segstats[seg];
                Vector3 p = CorePosition[i].pos;
                float x = p.X, y = p.Y, z = p.Z;
                float w = /* Change[i] * */ Hitf(CorePosition[i]);
                s.Add(x, y, z, w);

            }
            StageEnd("SegementStats");
            // leave the eigen computation to the client, we already have the classes available.
        }

    }       // class Program
}
