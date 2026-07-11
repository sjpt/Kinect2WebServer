#pragma warning disable IDE0040, IDE0049, CA1805, CA1304, CA1305, IDE0008, IDE0055, IDE0058, IDE0048, IDE0011

using MathNet.Numerics.Statistics;
using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using static Kinect2WebServer.Program;

    public static class Vector3Extension {
        public static void Deconstruct(this Vector3 v, out float x, out float y, out float z) { x = v.X; y = v.Y; z = v.Z; }
    }

namespace Kinect2WebServer {
    partial class Program {
        static readonly Vector3 NaNV = new(Single.NaN);
        static readonly VecPlus NaNVP = new VecPlus(NaNV, 0);
        protected static int coreWidth, coreHeight;
        static float coreStepx = 999;               // step from one core square to next; they may overlap, set on resolution choice from coreWidthTarget
        static float coreStepy = 999;               // step from one core square to next; they may overlap, set on resolution choice from coreWidthTarget
        static int coreStepn, coreStepn2;           // size of each core square
        static int coreWidthTarget = 32;            // target number of core slots in x
        static int corexx = 2;                      // extra core size to give overlap
        static bool eigenNormals = true;            // compensation for z scale in core stats, NB other values do not work 25 Apr 2024

        protected static VecPlus[] CorePosition;    // filtered depth data as sent to client in 'normal' use
        static Vector3[] CoreColours;               // colours for debug
        static VecPlus[] CoreVelocity;              // velocity data derived at CorePosition level
        static VecPlus[] CoreTwist;                 // twist data derived at CorePosition level
        static VecPlus[] OldCorePosition;           // previous frame of filtered depth data as sent to client in 'normal' use
        protected static float[] CoreChange;        // current change data, for damping, # pixels change in core slot
        static float[] CoreEdges;                   // count of edges from front in core slot, really int, but float easier to send for now
        //:@~ 2 Dec 2024. We were keeping track of goodn and backn, but not using them seriously.
        //:@~ If we are going to make more use of geometry grid  they become less well defined, so removing them with tad :@~ unless we find we do want them back.
        //:@~static int[] CoreGoodn;               	    // number of good hits in core slot
        //:@~static int[] CoreBackn;               	    // number of background hits in core slot
        static int[] CoreFrontn;              	    // number of hits to 'used' depth in core slot
        static int[] CoreNcount;                    // number of near neighbours seen
                                                    // nb CorePosition[co].v is multiplex of CoreFrontn (as fraction) and CoreChange (as integer)
        static float[] CoreMind;                    // minimum depth in slot
        static Vector3[] CoreCampos;                // centre pos in cam coords for core slot
        static Vector3[] CoreOffset;                // offset between slot centre and slot midpoint

        static Statsxyz[] CoreStatsCam;                // xyzstats for core, camera coords
        static Statsxyz[] CoreStatsGeom;                // xyzstats for core, geometry coords
        // static Statsxyz[] CoreTrackStats;           // xyzstats for core tracked regions
        static float[] _FilterChange;               // working data for CoreFilter (swapped in and out with Change)
        static float[] OldChange;                   // last change data, for damping

        static Matrix4x4 camMat; // = new (999,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0);  // matrix to test for point in camera view
        static float camviewRange = 999;

        static float coreFilterChangeWeight = 20;   // weight of active neighbours required to allow a core point to count as changed
        static float coreFilterPosWeight = 1f;      // total good hits (relative to coreSize**2)in neighbours for filtering positions
        static bool UseGeometryGrid = false;           // true to use a geometry based grid

        static int round(float f) { return (int)Math.Round(f); }
        static int ceil(float f) { return (int)Math.Ceiling(f); }
        static int floor(float f) { return (int)Math.Floor(f); }

        static int clox(int cx) { return floor(cx * coreStepx); }
        static int chix(int cx) { return clox(cx) + coreStepn; }
        static float ccenx(int cx) { return (clox(cx) + chix(cx) - 1) * 0.5f; }
        static int cloy(int cy) { return floor(cy * coreStepy); }
        static int chiy(int cx) { return cloy(cx) + coreStepn; }
        static float cceny(int cy) { return (cloy(cy) + chiy(cy) - 1) * 0.5f; }

        static void CoreAllocate() {
            coreWidth = coreWidthTarget;
            coreHeight = floor(coreWidthTarget * height / (float)width);
            coreStepx = (width - corexx) / (float)coreWidth;
            coreStepy = (height - corexx) / (float)coreHeight;
            coreStepn = floor(Math.Min(coreStepx, coreStepy)) + corexx;
            coreStepn2 = coreStepn * coreStepn;

            CorePosition = new VecPlus[coreWidth * coreHeight];  Array.Fill(CorePosition, NaNVP); // x,y,z,  val/change
            CoreColours = new Vector3[coreWidth * coreHeight];   // r,g,b
            OldCorePosition = new VecPlus[coreWidth * coreHeight]; Array.Fill(OldCorePosition, NaNVP);   // x,y,z,  val/change
            CoreVelocity = new VecPlus[coreWidth * coreHeight];
            CoreTwist = new VecPlus[coreWidth * coreHeight]; // x,y,z axes, scale
            CoreChange = new float[coreWidth * coreHeight];
            _FilterChange = new float[coreWidth * coreHeight];
            CoreEdges = new float[coreWidth * coreHeight];
            //:@~CoreGoodn = new int[coreWidth * coreHeight];
            //:@~CoreBackn = new int[coreWidth * coreHeight];
            CoreFrontn = new int[coreWidth * coreHeight];
            CoreNcount = new int[coreWidth * coreHeight];
            CoreMind = new float[coreWidth * coreHeight];
            CoreCampos = new Vector3[coreWidth * coreHeight];
            CoreOffset = new Vector3[coreWidth * coreHeight];
            CoreStatsCam = new Statsxyz[coreWidth * coreHeight];
            CoreStatsGeom = new Statsxyz[coreWidth * coreHeight];
            // CoreTrackStats = new Statsxyz[coreWidth * coreHeight];
            OldChange = new float[coreWidth * coreHeight];  // n, sumd, sumx, sumy
        }

        // I would like to keep Statsxyz smaller and extent it to FullStats with extra details (such as edges)
        // but C# does not seem to support that (despite what ChatGPT says)
        // public struct FullStats : Statsxyz { public float edges; } 
        /** used by Core and Segment */
        public struct Statsxyz {
            public float n, sw, sx, sy, sz, sxx, syy, szz, sxy, syz, szx;
            public float xx, yy, zz, xy, yz, zx, cx, cy, cz;
            // below would be better in an substruct if implemented
            public float edges;

            /** compute derived 'centred' xx etc, for  centre, n, and xyd=>xyz geometry (toGeometry)  */
            public void Compensate() {
                // 'compensated' sums for more convenient stats
                //float px = ix - width / 2, py = iy - height / 2, md = iz / 1000;  // average positions centred px, py in pixels, md distance in metres
                //mx = px * md * iddx; my = -py * md * iddy;             // notr sure why - for x TODO

                // cc values are for basic compensation of depth and camera perspective
                float ccz = 1.0f / 1000.0f, cccz = sz / n * ccz, ccx = cccz * iddx, ccy = cccz * iddy;
                xx = (sxx - sx * sx / n) / n * ccx * ccx;
                yy = (syy - sy * sy / n) / n * ccy * ccy;
                zz = (szz - sz * sz / n) / n * ccz * ccz;
                xy = (sxy - sx * sy / n) / n * ccx * ccy;
                yz = (syz - sy * sz / n) / n * ccy * ccz;
                zx = (szx - sz * sx / n) / n * ccz * ccx;
                cx = sx / n; // * ccx;
                cy = sy / n; // * ccy;
                cz = sz / n; // * ccz;
            }

            /** clear/reset values */
            public void Clear() {
                n = sw = sx = sy = sz = sxx = syy = szz = sxy = syz = szx = 0;
                xx = float.NaN; edges = 0;
                xx = float.NaN; edges = 0;
            }
            /** compute some slopes */
            public double[] Slopes() {
                // 'compensated' sums for more convenient stats
                if (float.IsNaN(xx)) Compensate();
                double[] r = new double[] { zx / xx, yz / yy, zx / zz, yz / zz, xy }; // / n / n };
                return r;
            }
            public Statsxyz Add(float rx, float ry, float rz) {
                n++;
                sw++;
                sx += rx;
                sy += ry;
                sz += rz;
                sxx += rx * rx;
                syy += ry * ry;
                szz += rz * rz;
                sxy += rx * ry;
                syz += ry * rz;
                szx += rz * rx;
                return this;
            }

            public Statsxyz Add(float rx, float ry, float rz, float w) {
                float w2 = w * w;
                n++;
                if (w != 0) {  // avoid NaN values upsetting weight w
                    sw += w;
                    sx += w * rx;
                    sy += w * ry;
                    sz += w * rz;
                    sxx += w2 * rx * rx;
                    syy += w2 * ry * ry;
                    szz += w2 * rz * rz;
                    sxy += w2 * rx * ry;
                    syz += w2 * ry * rz;
                    szx += w2 * rz * rx;
                }
                return this;
            }

            public Statsxyz Add(Vector3 g) {
                return Add(g.X, g.Y, g.Z);
            }
        }  // Statsxyz

        /** transform single depth reading to 3d coords
         * We do our own basic camera transformation for K2, and optionally for Azure.
         * Alternatively we use the Azure builtin transformation for basic camera.
         * (https://docs.microsoft.com/en-us/azure/kinect-dk/use-calibration-functions)
         * The Azure calculation allows for a 6degree tilt on the depth camera
         * (https://docs.microsoft.com/en-us/azure/kinect-dk/coordinate-systems)
         * and ? what else ... even allowing for that it doesn't map ix = width/2 => mx = 0 etc.
         * It probably knows soenthing we don't?
         * In any case, we aren't too concerned with absolute values as long as things look pretty much right.
         * In either case, we then apply kinCamMat, whch is set from the client based on the declared position/orientation of the camera.
         *
         * It might be more efficient in some cases to apply this in bulk,
         * but it is most often used for derived values so probably not appropriate.
         */
    private static Vector3 ToGeometry(float ix, float iy, float iz) {
        if (iz == 0 || float.IsNaN(iz) || float.IsInfinity(iz)) return NaNV;                   // iz == 0 means no depth signal

        if (activeDevice.Calibration(ix, iy, iz, out float mx, out float my, out float mz)) {
            // Azure.Calibration has done its work
        } else {
            int oy = -22; // experimental centre offset, maybe to do with 6 degree tilt???
            float px = ix + 0.5f - width / 2, py = iy + oy + 0.5f - height / 2, md = iz / 1000;  // average positions centred px, py in pixels, md distance in metres
            mx = px * md * iddx; my = -py * md * iddy;             // not sure why - for x TODO
            if (activeDevice.Type() == "Kinect2") mx *= -1;
            mz = -md;
        }

        Vector4 r = Vector4.Transform(new Vector4(mx, my, mz, 1), kinCamMat);

        /// pending extra filter ... if (r.Y > 3.8) return NaNV;
        return new Vector3(r.X, r.Y, r.Z);
    }

        /** compare my 'trivial' calibration agaist standard Azure calibration */
        public static string checkCalibration(float ix, float iy, float iz) {
            bool s = Azure.UseAzureCalibration;
            Azure.UseAzureCalibration = true;
            Vector3 tp = ToGeometry(ix, iy, iz);
            Vector3 tp2 = ToGeometry(ix, iy, iz * 0.001f);
            Azure.UseAzureCalibration = false;
            Vector3 fp = ToGeometry(ix, iy, iz);

            Azure.UseAzureCalibration = true;
            Vector3 tn = ToGeometry(width - ix, height - iy, iz);
            Azure.UseAzureCalibration = false;
            Vector3 fn = ToGeometry(width - ix, height - iy, iz);

            Azure.UseAzureCalibration = s;
            string ss = $" xp {tp.X} {fp.X}  yp {tp.Y} {fp.Y}  z {tp.Z} {fp.Z}";
            ss += $"\n xn {tn.X} {fn.X}  yn {tn.Y} {fn.Y}  z {tn.Z} {fn.Z}";
            ss += $"\n xtot {tp.X - tn.X}  {fp.X - fn.X} ytot {tp.Y - tn.Y} {fp.Y - fn.Y}";
            ss += $"\n ratio2 x {tp2.X / tp.X} y {tp2.Y / tp.Y} z {tp2.Z / tp.Z}";
            Console.WriteLine(ss);
            return ss;
        }


        /** convert a geometry point to a grid bucket */
        private static bool ToGridBucket(Vector3 g, out int cx, out int cy, out int co) {
            if (!UseGeometryGrid) throw new Exception("call ToGridBucket without UseGeometryGrid");
            cx = cy = co = -999;
            cx = (int)Math.Round(g.X * coreWidth / gridX + coreWidth / 2);
            if (cx < 0 || cx >= coreWidth) return false;
            cy = (int)Math.Round((g.Y - gridY / 2) * coreHeight / gridY + coreHeight / 2);
            if (cy < 0 || cy >= coreHeight) return false;
            co = cx + cy * coreWidth;
            return true;
        }

        /** returns the centre of grid bucket cx, cy) */
        private static void GridBucketCen(int cx, int cy, out float ccx, out float ccy) {
            ccx = (cx - coreWidth / 2) * gridX  / coreWidth;
            ccy = (cy - coreHeight / 2) * gridY  / coreHeight + gridY / 2;
        }

        /** unit test for bucket */
        private static void checkgb(int cx, int cy) {
            GridBucketCen(cx, cy, out float ccx, out float ccy);
            ToGridBucket(new Vector3(ccx, ccy, 0), out int cxq, out int cyq, out int co);
            Console.WriteLine("cx cy {0} {1} => ccx ccy {2} {3} => cxq cyq {4} {5}", cx, cy, ccx, ccy, cxq, cyq);

            GridBucketCen(cx+1, cy+1, out float ccx1, out float ccy1);
            ToGridBucket(new Vector3(ccx1, ccy1, 0), out int cx1q, out int cy1q, out int co1);
            Console.WriteLine("+ 1  cx cy {0} {1} => ccx ccy {2} {3} => cxq cyq {4} {5}", cx+1, cy+1, ccx1, ccy1, cx1q, cy1q);

            float b = 0.5f + 1f/1024, l = 1 - b;
            ToGridBucket(new Vector3(ccx*b + ccx1*l, ccy*b + ccy1*l, 0), out int cxhq, out int cyhq, out int coh1);
            Console.WriteLine("+ half- {0}, {1} => => {2} {3}", cx, cy, cxhq, cyhq);

            ToGridBucket(new Vector3(ccx*l + ccx1*b, ccy*l + ccy1*b, 0), out int cxhhq, out int cyhhq, out int cohh1);
            Console.WriteLine("+ half+ {0}, {1} => => {2} {3}", cx, cy, cxhhq, cyhhq);




        }

        static int reducenum = -1;
        static float coresmooth = 0;    // no smoothing
        static float cofsmooth = 0;     // no normal smoothing
        static float coretraildamp = 0;  // trail, 0 is no trail
        //static int[] ccc = new int[8];
        /** reduce DepthData into CorePosition (3d position), Change, Goodn
         * CoreVelocity and CoreTwist
         * Computes them all even if only some are sent to and used by client. */
        static void CoreReduce() {
            if (reducenum == callnum) return; reducenum = callnum;
            Stage("Reduce");
            ComputeDepthEdge(); // TODO do this only when necessary
            if (UseGeometryGrid)
                CoreRemapGeometryGrid();
            else
                CoreRemapCameraGrid();
            //int i = 0; Array.Clear(ccc);
            //ccc[i++] = CountGood();
            //>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> TO CONSDIER, CorePosition is used both for graphics (especially man)
            // AND dynamics to stop pulling tadpoles out of scene.
            if (filterback) {
                CoreFilterCam();    // filter all core positions to remove those outside view camera frustum
                //ccc[i++] = CountGood();
                CoreFilterPos();    // filter basic core information for points with insufficient 'good' neighbours
                //ccc[i++] = CountGood();
            }
            CoreFilterChange();
            //ccc[i++] = CountGood();

            // TODO make this optional
            for (int i = 0; i < CorePosition.Length; i++) {
                var v = CorePosition[i].v % 1;
                CoreColours[i] = new Vector3(v, 1-v, CorePosition[i].pos.Y < 0.03 ? 1 : 0);
            }

            CoreSmooth();
            CoreDerivatives();
            //ccc[i++] = CountGood();
            StageEnd("Reduce");
            // 22 Dec 2023, region tracking disabled, not good for green man. TODO reintroduce with options?
            //Stage("TrackRegions");
            //CoreTrackAllRegions();
            //StageEnd("TrackRegions");
            //ccc[i++] = CountGood();
        }

        static float gridX = 4, gridY = 3; //, gridZ = 4;   // geometry widths in metres

        /** reduce DepthData into CorePosition (3d position), Change, Goodn */

        /** reduce DepthData into CorePosition (3d position), Change, Goodn */
        /** create a 'core' map based on camera rays
         * Inputs: CorePosition, OldCorePosition, DepthArr, DepthArrBack, DepthArrOld, DepthArrEdge
         * Outputs: CorePosition, CoreCampos, CoreOffset, CoreChange, CoreGoodn??, CoreBackn??

         * */
        static void CoreRemapCameraGrid() {
            // int _irthresh = 0; // Irthresh();


            (CorePosition, OldCorePosition) = (OldCorePosition, CorePosition);  // swap core buffers
            maxd = 0;
            Statsxyz gss = new();  // stats in geometry form

            for (int cy = 0; cy < coreHeight; cy++) {
                for (int cx = 0; cx < coreWidth; cx++) {
                    int co = cy * coreWidth + cx;
                    CoreEdges[co] = 0;

                    // First pass to find mind, so we can just use the pixels near the front of the slot*slot region
                    // This pass ignores background, may well find front object which is background
                    // If ir is used for reliability if updates DepthArr to 0 for unreliable pixels
                    float mind = Single.PositiveInfinity;
                    for (int xx = clox(cx); xx < chix(cx); xx++) {
                        for (int yy = cloy(cy); yy < chiy(cy); yy++) {
                            UInt16 zz = DepthArr[xx + yy * width];
                            //ushort ir = InfraredArr[xx + yy * width];
                            //if (ir < _irthresh) { zz = DepthArr[xx + yy * width] = 0; }
                            if (zz != 0 && zz < mind) mind = zz;
                        }
                    }

                    // now compute changes/ n.b. change is NOT compensated for irregular time intervals (timeDelta)
                    float schange = 0;      // sum of all changes
                    ref Statsxyz ss = ref CoreStatsCam[co];
                    ss.Clear(); gss.Clear();

                    /// second pass also more or less ignores background
                    for (int ry = cloy(cy); ry < chiy(cy); ry++) {
                        for (int rx = clox(cx); rx < chix(cx); rx++) {
                            var ro = rx + ry * width;
                            UInt16 zz = DepthArr[ro];
                            float zzb = DepthArrBack[ro] * backExtra;
                            if (zz == 0) {
                                // don't explicitly track badn
                            } else if (zz > zzb && filterback) {
                                //:@~backn++;
                            //
                            //  goodn++;    // ??? should back count as good ???
                            } else { // if (zz != 0 && zz < zzb) { // && ir > _irthresh
                                //:@~if (zz > zzb) backn++;
                                //:@~goodn++;
                                //if (tad4background && zz > zzb)
                                //    zz = DepthArr[ro] = Convert.ToUInt16(maxDepth); //  (zzb);

                                // compute change, if neither is 0
                                // 2 mph = 9 m/sec = 9000mm/sec = 300 mm per frame ... minChange
                                // Try to distinguish between forward movement (continuous) 
                                // and ray now hits different object (jump).
                                // If it's a different object we aren't too concerned how different.
                                double zzo = DepthArrOld[ro];
                                if (zzo != 0) {
                                    if (zzo > zzb) zzo = zzb;
                                    double d = Math.Abs(zz - zzo) - minChange;  
                                    if (d <= 0) {
                                        // d = 0;
                                    } else {
                                        if (d > maxChange) d = maxChange; 
                                        schange += (float)d; //  Math.Abs(zz - zzo) * 4000 / Math.Max(zz, zzo);
                                        //schange++;
                                    }
                                    // nchange++;
                                }

                                // keep track of 'average' position for the front object, and other stats
                                if (zz < maxDepth && zz < mind * minDepthExtra) { // this is where we identify a frontn contribution
                                    float zs = zz;
                                    ss.Add(rx, ry, zs); // nb this computes frontn as ss.n
                                    if (DepthArrEdge[ro] != 0) CoreEdges[co]++; // TODO copy equivalent to CoreRemapGeometryGrid

                                    if (eigenNormals) {  // also keep track of geometry stats if wanted
                                        Vector3 g = ToGeometry(rx, ry, zz);
                                        gss.Add(g);
                                    }
                                }   // end good
                            }   // zz != 0 ...

                            if (zz > maxd) maxd = zz;
                        } // yy
                    }   // xx
                completeCore(ss, gss, schange, cx, cy, co, mind, coreStepn2);
                } // cx
            } // cy
        }   // CoreRemapCameraGrid

        static private void completeCore(Statsxyz ss, Statsxyz gss, float schange, int cx, int cy, int co, float mind, float corerays) {
            // was (coreStepn2) not goodn here, goodn makes more sense near boundaries where there is lots of noise
            // though actually the 'noise' is a very good indicator of boundary
            //:@~float change = schange / Math.Max(goodn, 50); // may want nchange here?, 
            // float change = schange / Math.Max(ss.n, 50); // may want nchange here?, 
            // schange *= 0;  // !!! to follow up, change is used remarkably little in tadkin.js; ? only for growth in positionhigh/tadman ?

            float ffn = ss.n;
            CoreFrontn[co] = (int)ffn;
            if (ffn == 0 && !float.IsInfinity(mind))
                Debug("?empty");
            if (ffn == 0) {
                // not used nempty++;
                CoreCampos[co] = NaNV;
                CorePosition[co] = NaNVP;
                CoreOffset[co] = NaNV;
                return; // continue;
            }
            float change = schange / corerays;
            CoreCampos[co] = new(ss.sx / ffn, ss.sy / ffn, ss.sz / ffn);
            float depth = ss.sz / ffn;
            float hitp = ffn / corerays;          // hit proportion
            if (depth > maxDepth * 0.9) { depth = maxDepth; } // CoreChange[co] = change = 0; }
            CorePosition[co].pos = ToGeometry(ss.sx / ffn, ss.sy / ffn, depth);
            // n.b. the integer part will be put back in in CoreDerivatives
            // compensate change for distance, and to ignore fuly populated areas
            // -kinCamMat.M43 (typically 3) is z position of Kinect 2/11/24
            CoreChange[co] = (float) (change * Math.Pow(-kinCamMat.M43 + CorePosition[co].pos.Z, 2) * (1-hitp));
            CorePosition[co].v = (int)CoreChange[co] + Math.Min(0.997f, hitp);	// proportion of front, 0..1. and change as integer

            Vector3 ocof = CoreOffset[co];
            ref Vector3 cof = ref CoreOffset[co];
            if (eigenNormals && gss.n >= 3) {  // Eigen test for CoreOffset
                gss.Compensate();
                double[] eval; double[,] evec;
                (eval, evec) = Eigen(gss);
                int k = (eval[2] < eval[0]) ? (eval[2] < eval[1] ? 2 : 1) : (eval[1] < eval[0] ? 1 : 0); // smallest vector #, ?? always 0 ??
                cof = new Vector3((float)(evec[k, 0]), (float)(evec[k, 1]), (float)(evec[k, 2]));
                cof *= 2 * Math.Min(hitp, 1 - hitp);
            } else if (UseGeometryGrid) {            // centroid test for CoreOffset
                GridBucketCen(cx, cy, out float ccx, out float ccy);
                cof = new Vector3(gss.sx / ffn - ccx, gss.sy / ffn - ccy, 0);
                cof *= 2 * Math.Min(hitp, 1 - hitp) / cof.Length();
            } else {
                cof = new Vector3(ss.sx / ffn - ccenx(cx), ss.sy / ffn - cceny(cy), 0);
                cof *= 2 * Math.Min(hitp, 1 - hitp) / cof.Length();
            }

            if (cofsmooth != 0) {
                if (!Single.IsNaN(ocof.X + cof.X)) {
                    cof = (1- cofsmooth) * cof + cofsmooth * ocof;
                }
            }
        } // completeCore

        /** count the number of good core slots in CorePosition, for debug */
        static int CountGoodD() { int n = 0; for (int i = 0; i < CorePosition.Length; i++) if (CorePosition[i].v != 0) n++; return n; }

        // work out derivatives such as velocity/twist, compare to old
        static void CoreDerivatives() {
            float minGoodnI = minGoodn * coreStepn2;  // << todo refine for geometry grid
            int nnotwist = 0;

            for (int cy = 0; cy < coreHeight; cy++) {
                for (int cx = 0; cx < coreWidth; cx++) {
                    int co = cy * coreWidth + cx;

                    // damp change
                    float change = CoreChange[co];
                    float _oldChange = OldChange[co];
                    if (change < _oldChange) change = _oldChange * changeDamp + change * (1F - changeDamp);
                    OldChange[co] = change;

                    // save into buffer, leave as was if no relable data (??? to work with damped change, but ???)
                    // bool ok = n != 0;
                    bool ok = true;
                    if (CorePosition[co].v != 0)
                        Debug("non 0 CorePosition.v");
                    if (ok) {
                        // compute real geometry
                        float fractn = Hitf(CorePosition[co]);	// proportion of front, 0..1 (%1 not really needed, change not yet multiplexed in
                        float ofractn = Hitf(OldCorePosition[co]);
                        Vector3 pos = CorePosition[co].pos;
                        Vector3 opos = OldCorePosition[co].pos;

                        // '* minDepthExtra ' instead of '+ maxChange' does not work. z may be -ve, * minDepthExtra is for raw z/depth values
                        // to prevent something still counting as moving when something in front reveals it
                        // still leaves 'halo', eg round head as arm moved away from head
                        if (pos.Z > opos.Z + 0.2) // maxChange/1000)    // max change in raw depth units (mm)
                            OldChange[co] = change = 0;

                        // nb goodn is a measure of reliability ... may want to use more carefully
                        //??? frontn, ss.n is a more reliable measure??
                        
                        //:@~ CorePosition[co].v = ((CoreGoodn[co] < minGoodnI) ? 0 : (float)Math.Round(change)) + fractn;
                        CorePosition[co].v = ((CoreStatsCam[co].n < minGoodnI) ? 0 : (float)Math.Round(change)) + fractn;
                        // if (o == 309) CWriteLine($"{opos.Z} {pos.Z} {change}");

                        if (fractn * ofractn != 0)
                            Debug("non 0 fractn pair");
                        bool twist = Veltwist(co, CorePosition, CoreVelocity, CoreTwist, pos, opos, fractn * ofractn);
                        if (!twist) nnotwist++;
                    } else {
                        CorePosition[co].v = 0;
                    }
                    //Vector3 p = CorePosition[co].pos;
                    //if (float.IsNaN(p.X + p.Y + p.Z)) CorePosition[co].pos = Vector3.Zero; //####
                    if (float.IsNaN(CoreTwist[co].v + CoreTwist[co].pos.X)) { CoreTwist[co].pos = Vector3.Zero; CoreTwist[co].v = 0; }
                }   // y
            }   // x

            if (maxd > gmaxd) {
                CWriteLine($"maxd={maxd} global={gmaxd}");
                gmaxd = maxd;
            }
        }   // CoreDerivatives

        static void CoreSmooth() {
            // smooth
            if (coresmooth != 0) {
                float sm = coresmooth;
                int ook = 0;
                for (int i = 0; i < CorePosition.Length; i++) {
                    float ncpv = CorePosition[i].v, ncpi = (float)Math.Floor(ncpv), ncph = ncpv % 1;
                    float ocpv = OldCorePosition[i].v, ocpi = (float)Math.Floor(ocpv), ocph = ocpv % 1;
                    ref Vector3 ncpp = ref CorePosition[i].pos;
                    Vector3 ocpp = OldCorePosition[i].pos;
                    bool ogood = !float.IsNaN(ocpp.X), ngood = !float.IsNaN(ncpp.X);
                    float xcpi, xcph;
                    if (!ogood) {
                        continue;
                    } else if (ogood && !ngood) {  // decay phase
                        ncpp = ocpp;
                        xcpi = Math.Max(ocpi - 1, 0);
                        float smx = coretraildamp;
                        xcph = smx * ocph;  // ?? float smx for different decay trails
                        if (xcph < 0.03) { xcph = 0; CorePosition[i] = NaNVP; }
                    } else { // both good
                        if ((ncpp - ocpp).Length() > 0.05)  // don't try to smooth significant jump
                            continue;
                        ncpp = ocpp * sm + ncpp * (1 - sm);
                        xcpi = sm * ocpi + (1 - sm) * ncpi;
                        xcph = sm * ocph + (1 - sm) * ncph;
                    }
                    ook++;
                    float xcp = ceil(xcpi) + xcph;
                    if (float.IsNaN(xcp))
                        xcp += 0;   // debug
                    if (xcp != ncpv)
                        xcp += 0;   // debug

                    CorePosition[i].v = xcp;
                }
            }
        }

        /** filter all core positions to remove those outside view camera frustum */
        static void CoreFilterCam() {
            if (camviewRange == 999) return;
            // Matrix4x4 icamMat = Matrix4x4.Transpose(camMat);
            Stage("CoreFilterPos");
            float r = camviewRange;
            for (int cy = 0; cy < coreHeight; cy++) {
                for (int cx = 0; cx < coreWidth; cx++) {
                    int co = cx + cy * coreWidth;
                    Vector4 v4 = new Vector4(CorePosition[co].pos, 1);
                    if (float.IsNaN(v4.X)) continue;
                    Vector4 tr = Vector4.Transform(v4, camMat);
                    tr /= tr.W;
                    if (tr.X < -r || tr.X > r || tr.Y < -r || tr.Y > r) //  || tr.Z < -r || tr.Z > r)
                    if (tr.X < -r || tr.X > r || tr.Y < -r || tr.Y > r) //  || tr.Z < -r || tr.Z > r)
                        CorePosition[co] = NaNVP;
                }
            }
        }


        /* filter basic core information for points with insufficient 'good' neighbours */
        static void CoreFilterPos() {
            if (coreFilterPosWeight <= 0) return;
            if (UseGeometryGrid) return;  // todo check if this is sensible
            Stage("CoreFilterPos");
            for (int cy = 0; cy < coreHeight; cy++) {
                for (int cx = 0; cx < coreWidth; cx++) {
                    int co = cx + cy * coreWidth;       
                    var nfront = (cx == 0 ? 0 : CoreFrontn[co - 1]) +
                        (cx == coreWidth - 1 ? 0 : CoreFrontn[co + 1]) +
                        (cy == 0 ? 0 : CoreFrontn[co - coreWidth]) +
                        (cy == coreHeight - 1 ? 0 : CoreFrontn[co + coreWidth]);
                    if (nfront < coreStepn2 * coreFilterPosWeight) {
                        CorePosition[co] = NaNVP;
                        //:@~CoreGoodn[co] = 0;
                        CoreFrontn[co] = 0;
                        //:@~CoreBackn[co] = 999;
                    }
                }
            }
            StageEnd("CoreFilterPos");
        }



        /** filter the core data to remove isolated points, with less than coreFilterNum neighbours
         * filters into _FilterChange and swaps, so input is (unfilered) Change and output is (filtered) Change */
        static void CoreFilterChange() {
            if (coreFilterChangeWeight <= 0) return;      // just leave Change unchanged
            Stage("CoreFilterChange");
            for (int cy = 0; cy < coreHeight; cy++) {
                for (int cx = 0; cx < coreWidth; cx++) {
                    int co = cx + cy * coreWidth;
                    var nch = (cx == 0 ? 0 : CoreChange[co - 1]) +
                        (cx == coreWidth - 1 ? 0 : CoreChange[co + 1]) +
                        (cy == 0 ? 0 : CoreChange[co - coreWidth]) +
                        (cy == coreHeight - 1 ? 0 : CoreChange[co + coreWidth]);
                    _FilterChange[co] = (nch < coreFilterChangeWeight) ? 0 : CoreChange[co];
                }
            }
            (CoreChange, _FilterChange) = (_FilterChange, CoreChange);
            StageEnd("CoreFilterChange");
        }

        // static bool IsNaN(Vector3 v) { return float.IsNaN(v.X + v.Y + v.Z); }
        // establish if two core points are neighbours
        // TODO, edgeThresh is in wrong units, designed for raw camera depth (mm)
        // TODO, better to pass co (core ids) to this, and then use stats?
        static bool NotNeighbour(VecPlus p, VecPlus pa) {
            return pa.v == 0
                // || IsNaN(pa.pos)
                || Fract(pa.v) * coreStepn2 < 6	//  TEMP to verify
                || Fract(pa.v) * coreStepn2 == 0	//  TEMP to verify
                || !((pa.pos - p.pos).Length() < edgeThresh);  // this handles NaN in p or pa
        }


        /** find 'velocity' (the use of velocity for gridded data is a bit odd, but lets see if it works */
        // timeDelta used in computation of velocity to compensate for irregular time intervals
        // velocity is (sort of) in metres per second
        // return false if no neighbours found
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static bool Veltwist(int co, VecPlus[] Position, VecPlus[] Velocity, VecPlus[] Twist, Vector3 pos, Vector3 opos, float usefrac) {
            // Twist[co].pos = new();        // temp debug
            // Velocity[co].pos = new();     // temp debug
            bool twset = false;             // set true if we do set twist
            Twist[co].v = 0;
            Twist[co].pos = Vector3.Zero;
            Velocity[co].v = 0;
            Velocity[co].pos = Vector3.Zero;

            if (usefrac == 0)
                return false;

            // Vector3 ovel = Velocity[co].pos;
            Vector3 vel = Position[co].v == 0 || usefrac == 0 ? Vector3.Zero : (pos - opos) / timeDelta;
            if (Math.Abs(vel.Z) > 10) vel = Vector3.Zero;   // moved from one 'front' depth to another, ? why not depthChangeThresh
            Velocity[co].pos = vel *= usefrac;
            if (float.IsNaN(Velocity[co].pos.X))
                Debug("NaN in Velocity for Veltwist");
            if (Velocity[co].pos.X > 0)
                Debug("non 0 Velocity for Veltwist");
            vel = Position[co].pos - OldCorePosition[co].pos;
            Velocity[co].v = vel.Length();
            //Vector3 twist = Vector3.Cross(vel, ovel);
            //Twist[co].pos = twistDamp * Twist[co].pos + (1 - twistDamp) * twist;
            //Twist[co].v = Twist[co].pos.Length();
            //if ((float.IsNaN(Twist[co].pos.X) || float.IsNaN(Velocity[co].pos.X)) && usefrac != 0)
            //    Debug("NaN in Twist/Vecocity for Veltwist");

            // experiment with static angled change for twist
            var po = Position[co];
            if (float.IsNaN(po.v))
                Debug("Non position.v to twist");
            //if (po.v != 0) {
            if (Hitf(po) != 0) {
                if (po.v == -1234) {
                    // this PENDING version to do multi-dimensional twists
                    // based on surface normal computed from cross of neighbour direction vectors
                    //
                    // TODO
                    // looks as if it is basically OK, but gets dominated by large body areas
                    // needs help (eg number of neighbours)
                    Vector3 difab, diflr;
                    // float twistab = 0, twistlr = 0;
                    int cx = co % coreWidth;
                    int cy = co / coreWidth;
                    int oa = cy == 0 ? 0 : co - coreWidth;
                    int ob = cy == coreHeight - 1 ? co : co + coreWidth;
                    VecPlus pa = Position[oa];
                    if (NotNeighbour(po, pa)) { oa = co; pa = po; }
                    VecPlus pb = Position[ob];
                    if (NotNeighbour(po, pb)) { ob = co; pb = po; }
                    if (ob - oa != 0) {
                        // check using change (either via change[co] or pos-opos, plus damping
                        //float twist= (pb.pos.z - pa.pos.z) / ((pb.pos - pa.pos).length()) * usefrac * (1 + (pos-opos).length()*1000);
                        //twist[co].v = twistdamp * twist[co].v + (1 - twistdamp) * twist;
                        //twistab = (pb.pos.Z - pa.pos.Z) / (pb.pos - pa.pos).Length() * usefrac;
                        difab = pb.pos - pa.pos;
                    } else {
                        return false; //  debug("no above/below");
                    }
                    if (float.IsNaN(Twist[co].pos.X) || float.IsNaN(Twist[co].v))
                        Debug("NaN in Twist/Vecocity for Veltwist");

                    int ol = cx == 0 ? 0 : co - 1;
                    int or = cx == coreWidth - 1 ? co : co + 1;
                    VecPlus pl = Position[ol];
                    if (NotNeighbour(po, pl)) { ol = co; pl = po; }
                    VecPlus pr = Position[or];
                    if (NotNeighbour(po, pr)) { or = co; pr = po; }
                    if (or - ol != 0) {
                        // check using change (either via Change[co] or pos-opos, plus damping
                        //float twist= (pb.pos.Z - pa.pos.Z) / ((pb.pos - pa.pos).Length()) * usefrac * (1 + (pos-opos).Length()*1000);
                        //Twist[co].v = twistDamp * Twist[co].v + (1 - twistDamp) * twist;
                        //twistlr = (pr.pos.Z - pl.pos.Z) / (pr.pos - pl.pos).Length() * usefrac;
                        diflr = pr.pos - pl.pos;
                    } else {
                        return false;
                    }
                    difab = Vector3.Normalize(difab);
                    diflr = Vector3.Normalize(diflr);
                    Vector3 cr = Vector3.Cross(diflr, difab);
                    float len = cr.Length();
                    if (len == 0) return false;
                    Twist[co].v = (float)(len * Math.Pow(Hitf(pa) * Hitf(pb) * Hitf(pl) * Hitf(pr), 0.01) * Hitf(po) / 100);
                    // Twist[co].pos = Vector3.Normalize(cr)
                    Vector3 ctw = new(cr.Y * cr.Z, cr.Z * cr.X, cr.X * cr.Y);
                    Twist[co].v *= ctw.Length();
                    Vector3 rbase = Vector3.Normalize(ctw);
                    Twist[co].pos = rbase;

                }
                else if (oldtwist) { // compute delta/slope values by looking at neightbours and offset of core slot centroid
                    float twistab = 0, twistlr = 0;
                    int cx = co % coreWidth;
                    int cy = co / coreWidth;
                    int oa = cy == 0 ? 0 : co - coreWidth;
                    int ob = cy == coreHeight - 1 ? co : co + coreWidth;
                    VecPlus pa = Position[oa];
                    if (NotNeighbour(po, pa)) { oa = co; pa = po; }
                    VecPlus pb = Position[ob];
                    if (NotNeighbour(po, pb)) { ob = co; pb = po; }
                    if (ob - oa != 0) {
                        // check using change (either via Change[co] or pos-opos, plus damping
                        //float twist= (pb.pos.Z - pa.pos.Z) / ((pb.pos - pa.pos).Length()) * usefrac * (1 + (pos-opos).Length()*1000);
                        //Twist[co].v = twistDamp * Twist[co].v + (1 - twistDamp) * twist;
                        twistab = (pb.pos.Z - pa.pos.Z) / (pb.pos - pa.pos).Length() * usefrac;
                    } else {
                        Debug("no above/below");
                    }
                    if (float.IsNaN(Twist[co].pos.X) || float.IsNaN(Twist[co].v))
                        Debug("NaN in Twist/Vecocity for Veltwist");

                    int ol = cx == 0 ? 0 : co - 1;
                    int or = cx == coreWidth - 1 ? co : co + 1;
                    VecPlus pl = Position[ol];
                    if (NotNeighbour(po, pl)) { ol = co; pl = po; }
                    VecPlus pr = Position[or];
                    if (NotNeighbour(po, pr)) { or = co; pr = po; }
                    if (or - ol != 0) {
                        // check using change (either via Change[co] or pos-opos, plus damping
                        //float twist= (pb.pos.Z - pa.pos.Z) / ((pb.pos - pa.pos).Length()) * usefrac * (1 + (pos-opos).Length()*1000);
                        //Twist[co].v = twistDamp * Twist[co].v + (1 - twistDamp) * twist;
                        twistlr = (pr.pos.Z - pl.pos.Z) / (pr.pos - pl.pos).Length() * usefrac;
                    }

                    float dx = CoreCampos[co].X / coreStepx - (coreStepx - 1) / (2f * coreStepx) - cx;  // ??????????????????
                    float dy = CoreCampos[co].Y / coreStepy - (coreStepy - 1) / (2f * coreStepy) - cy;
                    if (dx > 99999)
                        Debug("test");
                    Twist[co].v = dx * dy; //  * CoreGoodn[co]/(coreStep*coreStep); //  * not; //  * (Math.Abs(twistlr) + Math.Abs(twistab));
                    Twist[co].pos = Vector3.Normalize(new Vector3(twistab, twistlr, 1));
                } else {    // compute delta/slope values from stats of core slot
                    Statsxyz ss = CoreStatsCam[co];
                    // double[] eigen = ss.Eigenvalues(); // no use without eigenvectors ... ready when we get round to it
                    double[] slopes = ss.Slopes();
                    float twistab = (float)slopes[0] * 0.03f;
                    float twistlr = (float)slopes[1] * 0.03f;

                    // float not = or == ol || oa == ob ? 0 : 1;   // experiment to reduce spurious background twist
                    Twist[co].v = (float)-slopes[4] * Math.Min(ss.n, 50) / 50;
                    Twist[co].pos = Vector3.Normalize(new Vector3(twistab, twistlr, 1)); //?? (float)slopes[2] * 0.03f));
                    if (twistlr != 0 && twistab != 0)   
                        Debug("test");
                    float tt = 0.01f;
                    if (Math.Abs(Twist[co].v) > tt) {
                        Debug("test");
                        Twist[co].v = -Math.Sign(slopes[4] * tt);
                    }
                }
                twset = true;
            }
            return twset;
        }

        /** sensible version of Fract, works right for -ve numbers */
        static float Fract(float x) { return x - MathF.Floor(x); }
        protected static float Hitf(VecPlus x) { return x.v - MathF.Floor(x.v); }
        static int _dd(int x, int y, int x2, int y2) { return (x - x2) * (x - x2) + (y - y2) * (y - y2); }

        static float zmaxd = 0.1f;
        static float dist2 = 20;

        /** check for 'new' point x1,x1 close to x2,y2 and in range of xr, yr */
        static bool close_inrange(int x1, int y1, int x2, int y2, int xr, int yr) {
            if (_dd(x1, y1, xr, yr) > dist2) return false;
            var cp1 = CorePosition[x1 + y1 * coreWidth];
            if (cp1.v == 0) return false;
            var cp2 = CorePosition[x2 + y2 * coreWidth];
            return (Math.Abs(cp1.pos.Z - cp2.pos.Z) < zmaxd);
        }

        // public struct I2 { public int ci, cj; public I2(int i, int j) { ci = i; cj = j; } };
        public class LI2 : List<int[]> { };
        //???public struct P { public int ci, cj, count; public double[] e; public LI2 found; public string sfound; }

        static void CoreRemapGeometryGrid()
        {
            // int _irthresh = 0; // Irthresh();


            (CorePosition, OldCorePosition) = (OldCorePosition, CorePosition);  // swap core buffers
                                                                                // (Change, OldChange) = (OldChange, Change);                       // do NOT swap change buffers, OldChange copied from change as we go
            maxd = 0;
            Array.Fill(CoreEdges, 0);
            Array.Fill(CoreFrontn, 0);
            Array.Fill(CoreChange, 0);
            Array.Fill(CoreMind, Single.PositiveInfinity);
            for (int co = 0; co < CorePosition.Length; co++)
            {
                ref VecPlus cp = ref CorePosition[co];
                cp.pos.X = cp.pos.Y = cp.pos.Z = cp.v = 0;
                CoreStatsCam[co].Clear();
                CoreStatsGeom[co].Clear();
                // do NOT clear CoreOffset, old value is used in smoothing
            }

            /** get the depth for raw point, find geometry, bucket it and check for relevance */
            bool getd(int rx, int ry, out ushort zz, out float zzb, out int co, out Vector3 g)
            {
                co = -999; g = Vector3.One;
                int ro = rx + ry * width;       // index into raw
                zz = DepthArr[ro];
                zzb = DepthArrBack[ro] * backExtra;
                float zzx = zzb;// - 1;
                if (tad4background && (zz > zzx || zz == 0)) zz = DepthArr[ro] = (ushort)(zzb == 0 ? maxDepth : zzx);
                if (zz == 0 || (zz > zzb && zzb != 0))
                {
                    if (tad4background) {
                        zz = DepthArr[ro] = (ushort)(zzb == 0 ? maxDepth : zzb);
                    } else {
                        return false; // ignore bad and background
                    }
                }

                g = ToGeometry(rx, ry, zz);
                if (!ToGridBucket(g, out int cx, out int cy, out co)) return false;  // find cx,cy grid, ignore if out of grid
                return true;
            }

            // find front for each bucket, traverse all camera pixels
            for (int rx = 0; rx < width; rx++) {
                for (int ry = 0; ry < height; ry++) {
                    if (!getd(rx, ry, out ushort zz, out float zzb, out int co, out Vector3 g)) continue;
                    CoreMind[co] = Math.Min(CoreMind[co], zz);
                }
            }

            // collect bucket info, retraverse all camera pixels
            for (int rx = 0; rx < width; rx++) {
                for (int ry = 0; ry < height; ry++) {
                    if (!getd(rx, ry, out ushort zz, out float zzb, out int co, out Vector3 g)) continue;
                    int ro = rx + ry * width;       // index into raw

                    // compute change, allows for change from 0 => used, but NOT from used => 0
                    double zzo = DepthArrOld[ro];
                    if (zzo > zzb) zzo = zzb;
                    double d = Math.Abs(zz - zzo) - minChange;
                    if (d < 0) d = 0; if (d > maxChange) d = maxChange;
                    CoreChange[co] += (float)d; //  Math.Abs(zz - zzo) * 4000 / Math.Max(zz, zzo);

                    // keep track of 'average' position for the front object, and other stats
                    if (zz < maxDepth && zz < CoreMind[co] * minDepthExtra) {
                        // not sure how useful raw camera stats are when grouping in real geometry space
                        float zs = zz;
                        CoreStatsCam[co].Add(rx, ry, zs);
                        CoreStatsGeom[co].Add(g);
                        if (zz > maxd) maxd = zz;
                    }  // good front hit
                }   // ry
            } // rx

            // pass over all core slots and process/summarize information accumated above
            for (int cy = 0; cy < coreHeight; cy++)
            for (int cx = 0; cx < coreWidth; cx++) {
                int co = cy * coreWidth + cx;
                float mind = CoreMind[co];
                Statsxyz ss = CoreStatsCam[co];
                Statsxyz gss = CoreStatsGeom[co];

                double dfac = (mind*mind / 1000000); // multiplier to allow for depth 
                float raysPerThisSlot = (float) (raysPerSlotxy / dfac); // full #rays to this slot
                float schange = CoreChange[co];
                completeCore(ss, gss, schange, cx, cy, co, mind, raysPerThisSlot);
                /**
                 msgfix('!chit', () => `<b style="font-size: 900%">${tadkin.corePositionArr.filter((x,i) => i%4===3 && x).map(x => x%1).reduce((c,v) => Math.max(c,v), 0).toFixed(3)}</b>`)
                 msgfix('!cchange', () => `<b style="font-size: 900%">${tadkin.corePositionArr.filter((x,i) => i%4===3 && x).map(x => Math.floor(x)).reduce((c,v) => Math.max(c,v), 0).toFixed(3)}</b>`)
                 * */
            }
        }   // CoreRemapGeometryGrid


        ///* track a row j=cj left and right from start csi, return centre i */
        //static int CoreTrackRow(int csi, int cj, int ssi, int ssj, ref int count, LI2 found) {
        //    void sumstats(int ci, int cj, ref int count) {
        //        int cox = ci + coreWidth * cj;
        //        ref Statsxyz ss = ref CoreTrackStats[ssi + ssj * coreWidth];
        //        Statsxyz nn = CoreStats[cox];
        //        ss.n += nn.n;
        //        ss.sx += nn.sx;
        //        ss.sy += nn.sy;
        //        ss.sz += nn.sz;
        //        ss.sxx += nn.sxx;
        //        ss.syy += nn.syy;
        //        ss.szz += nn.szz;
        //        ss.sxy += nn.sxy;
        //        ss.syz += nn.syz;
        //        ss.szx += nn.szx;
        //        ss.edges += CoreEdges[cox];
        //        count++;
        //        found.Add(new int[] { ci, cj });
        //    }
        //    sumstats(csi, cj, ref count);

        //    int cli, cri;   // cli, cri end up just outside the region
        //    for (cri = csi+1; cri < coreWidth; cri++) {     // scan right
        //        if (!close_inrange(cri, cj, cri - 1, cj, ssi, ssj)) break;
        //        sumstats(cri, cj, ref count);
        //    }
        //    for (cli = csi - 1; cli >= 0; cli--) {          // scan left
        //        if (!close_inrange(cli, cj, cli + 1, cj, ssi, ssj)) break;
        //        sumstats(cli, cj, ref count);
        //    }
        //    return (cli + cri) / 2;  // take the middle, it will be used as starting i in the rows above and below
        //}
        ///** track a region from a start point */
        //static int CoreTrackRegion(int csi, int csj, LI2 found) {
        //    int count = 0;
        //    int ci0 = CoreTrackRow(csi, csj, csi, csj, ref count, found);
        //    int ci = ci0;
        //    for (int cj = csj+1; cj < coreHeight; cj++) {   // scan up
        //        if (!close_inrange(ci, cj, ci, cj-1, csi, csj)) break;
        //        ci = CoreTrackRow(ci, cj, csi, csj, ref count, found);
        //    }
        //    ci = ci0;
        //    for (int cj = csj - 1; cj >= 0; cj--) {         // scan down
        //        if (!close_inrange(ci, cj, ci, cj + 1, csi, csj)) break;
        //        ci = CoreTrackRow(ci, cj, csi, csj, ref count, found);
        //    }
        //    return count;
        //}

        ///** track regions around each core point and look for 'interesting' patterns (eg ellipsoids?) 
        // * First very basic step towards feature recognition.
        // */
        //static int CoreTrackAllRegions() {
        //    int co = 0;
        //    int count = 0;
        //    P[] evs = new P[10000]; int vo = 0;
        //    for (int cj = 0; cj < coreHeight; cj++)
        //        for (int ci = 0; ci < coreWidth; ci++) {
        //            ref var cts = ref CoreTrackStats[co];
        //            cts.Clear();
        //            ref var cpo = ref CorePosition[co];
        //            if (cpo.v == 0) { co++; continue; }
        //            if (float.IsNaN(cpo.pos.X)) { co++; continue; }  // should be caught by v == 0?
        //            LI2 found = new();
        //            count += CoreNcount[co] = CoreTrackRegion(ci, cj, found);
        //            if (cts.n == 0) {
        //                co = co + 0;// should not happen
        //            } else {
        //                double[] eval; double[,] evec;
        //                cts.Compensate();
        //                (eval, evec) = Eigen(cts); //
        //                // eval = cts.Eigenvalues();  // will also complete/compensate cts: cx, etc etc 
        //                if (eval[0] > eval[1] || eval[1] > eval[2])
        //                    Console.Error.WriteLine("unexpected eigenvalue order");
        //                // Array.Sort(eval); it seems to come presorted with biggest ee[2] last 
        //                P pp;
        //                pp.e = eval; pp.ci = ci; pp.cj = cj; pp.count = CoreNcount[co]; pp.found = found;
        //                pp.sfound = string.Join("  ", pp.found.Select(i2 => i2[0].ToString() + "," + i2[1].ToString()));
        //                evs[vo++] = pp;
        //                // MathNet.Numerics.LinearAlgebra.Matrix<double> m = EigenVectors(CoreTrackStats[co]);

        //                var ctgeom = ToGeometry(cts.cx, cts.cy, cts.cz);
        //                var cdiff = ctgeom - cpo.pos;
        //                var ctdist = cdiff.Length();

        //                if (cj < coreHeight * 0.3)
        //                    pp.count += 0;
        //                //if (ee[1] > 0.0008) cpo = NaNVP;
        //                if (eval[2] < 4 * eval[1]) cpo = NaNVP;  // test for significant ellipsoid
        //                if (cj > coreHeight - 3) cpo = NaNVP;   // ignore the bottom ~ could be moved into the loop control for efficiency
        //                // double diffdot = (evec[0, 2] * cdiff.X + evec[1, 2] * cdiff.Y + evec[2, 2] * cdiff.Z);
        //                double diffdot = (evec[2, 0] * cdiff.X + evec[2, 1] * cdiff.Y + evec[2, 2] * cdiff.Z);  // offset correlated with long eigenvector
        //                // if (diffdot < 0.1) cpo = NaNVP;
        //                // if (cts.edges < 110) cpo = NaNVP;  // test for clenched
        //                // if (cts.n > 10 * coreStep * coreStep) cpo = NaNVP; // test for 'hits in region, will be big in big regions such as body
        //                // if (ctdist < 0.15) cpo = NaNVP;  // test for near edge so region centre of gravity different from my CG, esp end of hand?

        //                CoreColours[co].X = (float) eval[2] * 100;  // big eigenvalue
        //                CoreColours[co].Y = (float) diffdot * 10;   // offset of local and region centroids in big eigen direction
        //                CoreColours[co].Z = (float) ctdist * 10;    // offset of local and region centroids

        //            }
        //            co++;
        //    }
        //    return count;
        //}

    }   // class Program
}
