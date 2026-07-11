#pragma warning disable IDE0040, IDE0049, CA1805, CA1304, CA1305, IDE0008, IDE0055, IDE0058, IDE0048, IDE0011
#pragma warning disable IDE0130 // Namespace does not match folder structure
using System;
using System.Numerics;
using System.Text.RegularExpressions;
using Fleck;
namespace Kinect2WebServer {
    class Grid : Program {

        static VecPlus[] GridPosition;           // filtered depth data at grid level as sent to client in 'normal' use
        static VecPlus[] GridVelocity;           // velocity data derived at grid level
        static VecPlus[] GridTwist;              // twist data derived at grid level

        public static void GridMessage(IWebSocketConnection socket, string message) {
            if (message.Contains("grid")) {
                string k = PrepareGrid(message);
                if (message.Contains("gridposition")) Send(socket, "gridposition " + k, GridPosition);
                if (message.Contains("gridvelocity")) Send(socket, "gridvelocity " + k, GridVelocity);
                if (message.Contains("gridtwist")) Send(socket, "gridtwist " + k, GridTwist);
            }
        }


        /** prepare grid based data: main input CorePosition (in ReduceBlock)
        * output: GridPosition, GridVelocity, GridTwist
        */
        static void PrepareGrid(int size, int step) {
            int nx = (coreWidth - size) / step + 1;
            int ny = (coreHeight - size) / step + 1;
            if (GridPosition == null || GridPosition.Length != nx * ny) {  // first time in, or after change in grid
                GridPosition = new VecPlus[nx * ny];
                GridVelocity = new VecPlus[nx * ny];
                GridTwist = new VecPlus[nx * ny];
            }
            // VecPlus[] r = new VecPlus[nx * ny];
            // float[] rf = new float[nx * ny * 4];
            int go = 0;
            for (int yk = 0; yk < ny; yk++) {
                for (int xk = 0; xk < nx; xk++) {
                    VecPlus opos = GridPosition[go];
                    VecPlus pos = GridPosition[go] = ReduceBlock(xk * step, xk * step + size, yk * step, yk * step + size);
                    Veltwist(go, GridPosition, GridVelocity, GridTwist, pos.pos, opos.pos, pos.v * opos.v);
                    go++;
                }
            }
        }

        public static string PrepareGrid(string message) {
            var rx = new Regex("grid (\\d*) (\\d*)");
            MatchCollection matches = rx.Matches(message);
            if (matches.Count == 0) return null;
            //for (int i = 0; i < matches.Count; i++) {
            int i = 0;  // only one grid allowed for now
            int size = int.Parse(matches[i].Groups[1].Value);
            int step = int.Parse(matches[i].Groups[2].Value);
            PrepareGrid(size, step);
            //}
            return $"{size} {step}";
        }

        /** average position over region of core data, weighted by change */
        public static VecPlus ReduceBlock(int locx, int hicx, int locy, int hicy) {
            Vector3 spos = new();
            float schange = 0, shitprop = 0;
            for (int cy = locy; cy < hicy; cy++) {
                for (int cx = locx; cx < hicx; cx++) {
                    int co = cx + cy * coreWidth;
                    var cd = CorePosition[co];
                    float change = CoreChange[co]; // n.b. Change has been filtered by this point. // (float)Math.Fl oor(cd.v);
                    //var nch = (x == 0 ? 0 : FilterChange[o - 1]) +
                    //    (x == coreWidth-1 ? 0 : FilterChange[o + 1]) +
                    //    (y == 0 ? 0 : FilterChange[o - coreWidth]) +
                    //    (y == coreHeight-1 ? 0 : FilterChange[o + coreWidth]);
                    //if (nch == 0) change = 0;
                    float lhitprop = Hitf(cd);
                    shitprop += lhitprop;
                    // float change = (float)Math.Fl oor(cd.v);

                    if (float.IsInfinity(cd.pos.X))
                        CWriteLine("Infinity found in ReduceBlock");
                    if (float.IsNaN(cd.pos.X)) //  && change != 0)
                        CWriteLine("NaN found in ReduceBlock");
                    if (!float.IsNaN(cd.pos.X) && !float.IsInfinity(cd.pos.X)) {      // IsNaN should not be needed here, check
                        // change = (float)Math.Fl oor(cd.v);
                        // change *= change;
                        float cc = change * lhitprop + 0.000001f;
                        spos += cd.pos * cc;
                        schange += cc;
                    }
                }
            }
            float ahitprop = shitprop / (hicx - locx) / (hicy - locy);
            return new VecPlus(spos / schange, (float)Math.Round(schange) + ahitprop);
        }


    } // end class Grid
}