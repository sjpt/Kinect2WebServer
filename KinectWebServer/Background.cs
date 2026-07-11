#pragma warning disable IDE0040, IDE0049, CA1805, CA1304, CA1305, IDE0008, IDE0055, IDE0058, IDE0048, IDE0011

using System;
using System.IO;

namespace Kinect2WebServer {
    partial class Program {
        // static int backgroundSteps = 0; //  backgroundStepsMax = 100;   // only refine for backgroundStepsMax steps
        //public class BackgroundStats {
        //    public double dampedDepth = 0; // , damped0prop = 0;
        //    //  nok = 0, sx = 0, sxx = 0, min = 1e29 /*Double.PositiveInfinity*/, max = 0;
        //    public int ii;
        //}

        static ushort fixDepth = 0;    // if +ve, any non-background pixel set to this depth
        static int runs = 0;    // keep track of runs
        //static BackgroundStats[] backgroundStats;
        /** refine the background, can move back (slowly avoiding noise), but not forwards */
        static void RefineBackground() {

            //if (backgroundStats == null) {
            //    backgroundStats = new Stats[asize];
            //    for (var i = 0; i < asize; i++) { backgroundStats[i] = new(); backgroundStats[i].ii = i; }

            //}
            // if (backgroundSteps++ > backgroundStepsMax) return;
            for (var ry = 0; ry < height; ry++) {
                for (var rx = 0; rx < width; rx++) {
                    var ro = rx + ry * width;
                    UInt16 zz = DepthArr[ro];
                    // if (zz > 9000) zz = 0; // large noise values are handled in Azure GetDepthData()
                    // 1 Nov 2024, DepthArrBad used in client diagnostics, but not in server
                    DepthArrBad[ro] = DepthArrBad[ro] * baddamp + (zz == 0 ? 1 : 0) * (1 - baddamp);
                    if (zz < backMin && fixDepth == 0) continue;
                    float zzb = DepthArrBack[ro];
                    // if (zzb > 5000) zzb = 0;  // large noise values are handled in Azure GetDepthData()
                    // BackgroundStats s = backgroundStats[ro];
                    // zzb should be same (apart from float/double) as s.dampedDepth
                    // s.n++;
                    if (zz == 0) {
                        // s.nzero++;
                    } else {
                        // these are not used for now
                        //s.nok++;
                        //s.sx += zz;
                        //s.sxx += zz * zz;
                        //s.min = Math.Min(s.min, zz);
                        //s.max = Math.Max(s.max, zz);

                        if (zz > zzb * backExtra) {
                            zzb = zzb == 0 ? zz : (zzb * backDamp + zz * (1 - backDamp));
                            zz = 0;
                        }
                        if (fixDepth > 0) 
                            zz = DepthArr[ro] = zz == 0 ? (ushort)0 : fixDepth;
                        // if (zzb * backExtra < zz && zz < zzb) DepthArrBack[xx + yy * width] = zz; // ? overfast background creep forward
                    }
                    DepthArrBack[ro] = zzb;
                    //                    s.damped0prop = (s.damped0prop * backDamp + (zz == 0 ? 1 : 0) * (1 - backDamp));
                    //                  DepthArrBack[ii] = s.damped0prop > 0.99 ? 0 : (float)s.dampedDepth; // w.i.p. odd shadows


                    //// test for less acceptance of background
                    //if (rx != 0 && DepthArr[ro - 1] == 0 || rx != width-1 && DepthArr[ro + 1] == 0 ||
                    //    ry != 0 && DepthArr[ro - width] == 0 || ry != height-1 && DepthArr[ro + width] == 0) {
                    //    // only accept non-background if surrounded by non-background
                    //} else {
                    //    DepthArrBack[ro] = zzb;
                    //}
                    // s.dampedDepth = zzb;
                    // backgroundStats[ro] = s;
                } // x
            } // y


            // crude median filter, only reducing overbig values that derive from Kinect noise
            var a = DepthArrBack;
            int k = (runs++ % 2 == 0) ? 1 : width;        // alternate vertical and horizontal filter
            for (var ry = 1; ry < height-1; ry++) {
                for (var rx = 1; rx < width-1; rx++) {
                    var ro = rx + ry * width;
                    var zz = a[ro];
                    var za = a[ro - k];
                    var zb = a[ro + k];

                    //var zc = a[ro - width];
                    //var zd = a[ro + width];
                    //if (zz > zc && zz > zd && zd != 0)
                    //    zz = zc > zd ? zd : zc;
                    if (zz > za && zz > zb)
                        zz = za > zb ? za : zb;
                    a[ro] = zz;
                }
            }
        }

        /** move the background forward by factor k distance d, it will then resettle */
        static void MoveBackground(float k, float d) {
            // for (var i = 0; i < DepthArrBack.Length; i++) /*DepthArrBack[i]*/ backgroundStats[i].dampedDepth *= k;
            for (var ro = 0; ro < DepthArrBack.Length; ro++)
                if (DepthArrBack[ro] != 0) DepthArrBack[ro] = DepthArrBack[ro] * k - d;
        }

        /** where current reading is just in front of background move background forward.
         * 'real' objects just in front of background will be lost (as with ResetBackground) 
         * but the majority of 'real' objects will remain distinct from the new background (unlike ResetBackground)
         */
        static void MoveLikelyBackground(float k) {
            for (var ro = 0; ro < DepthArrBack.Length; ro++) {
                float zz = DepthArr[ro];
                float zzb = DepthArrBack[ro];
                if (zz != 0 && zzb * k < zz && zz < zzb)    // zz is in the 'uncertainty' range just in front of the background
                    DepthArrBack[ro] = zz;                  // so we allow the background to move forwards
            }
        }

        static void ResetBackground() {
            Array.Fill(DepthArrBack, 0);
            // backgroundSteps = 0;
            // for (var ro = 0; ro < rsize; ro++) { backgroundStats[ro] = new(); backgroundStats[ro].ii = ro; }
        }

        /** set all 'unset' (always bad) values to maximum background depth */
        static void KillBackgroundBad() {
            for (var ro = 0; ro < rsize; ro++) {
                if (DepthArrBack[ro] == 0) DepthArrBack[ro] = maxDepth;
            }
        }

        /** save the background array to file */
        static void SaveBackground() {
            _ = Directory.CreateDirectory(localappdir);
            using (BinaryWriter binWriter =
                new(File.Open(backfid, FileMode.Create))) {
                Buffer.BlockCopy(DepthArrBack, 0, TempByteArray, 0, TempByteArray.Length);  // convert to byte ready for transfer
                binWriter.Write(TempByteArray);
            }
            CWriteLine($"Written background file '{backfid}'");
        }

        /** load the background array from file, and reset backgroundStats accordingly */
        static void LoadBackground() {
            backfid = $"{localappdir}\\background_{devid}.background";

            if (!File.Exists(backfid)) { CWriteLine($"cannot find background file {backfid}"); return; }
            using (BinaryReader binReader =
                new(File.Open(backfid, FileMode.Open))) {
                binReader.Read(TempByteArray);
            }
            Buffer.BlockCopy(TempByteArray, 0, DepthArrBack, 0, TempByteArray.Length);  // convert from byte
            //for (int i = 0; i < DepthArrBack.Length; i++) {
            //    var s = backgroundStats[i];
            //    s.dampedDepth = DepthArrBack[i];  // ?? should we set other stats fields ??
            //    // s.n = 1000;
            //    // s.nzero = 0;
            //}
            CWriteLine($"Read background file '{backfid}'");
        }


    }
}
