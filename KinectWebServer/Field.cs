#pragma warning disable IDE0040, IDE0049, CA1805, CA1304, CA1305, IDE0008, IDE0055, IDE0058, IDE0048, IDE0011

using System;
using System.Numerics;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

namespace Kinect2WebServer {
    partial class Program {
        static float[] FieldArr = new float[1];
        static Vector3[] FieldVec = new Vector3[1];
        static Vector3[] FieldVecOld = new Vector3[1];
        static Vector3[] FieldTwist = new Vector3[1];
        static Vector3[] FieldTwistSc = new Vector3[1];
        /** make 3d field, input DepthArr, output FieldArray */
        static void Field3(int nx = 30, int ny = 21, int nz = 21) {
            int s = nx * ny * nz;
            int ky = nx - 1, kz = (nx - 1) * (ny - 1);                // multipliers for multidimensional array
            float ratx = nx / width, raty = ny / width;             // ratio for raw and 3d arrays

            // float tand(float d) { return MathF.Tan(d * MathF.PI / 180); }
            float lz = 500;
            float hz = 8000;
            // float nz = 20;
            float llz = MathF.Log(lz);
            float lhz = MathF.Log(hz);
            float ldz = lhz - llz;
            float rz = nz / ldz;

            int sc = 3; // scale down
            int scnx = nx / sc, scny = ny / sc, scnz = nz / sc;
            int scky = scnx, sckz = scnx * scny;
            int ssc = scnx * scny * scnz;

            float f(float z) { return (MathF.Log(z) - llz) * rz; }

            Stage("make3d");
            //if (FieldArr.GetLength(0) != nx || FieldArr.GetLength(1) != ny || FieldArr.GetLength(2) != nz) FieldArr = new float[nx, ny, nz];
            // fill
            //for (int ix = 0; ix < nx; ix++) for (int iy = 0; iy < ny; iy++) for (int iz = 0; iz < nz; iz++) FieldArr[ix, iy, iz] = 0;
            if (FieldArr.Length != s) {
                FieldArr = new float[s];
                FieldVec = new Vector3[s];
                FieldVecOld = new Vector3[s];
                FieldTwist = new Vector3[s];
                FieldTwistSc = new Vector3[ssc];

            }
            Array.Fill(FieldArr, 0);

            // work over input data
            for (int ry = 0; ry < height; ry++) {
                for (int rx = 0; rx < width; rx++) {
                    ushort v = DepthArr[ry * width + rx];
                    if (v == 0) continue;
                    float x = rx * ratx, y = ry * raty;
                    float z = f(v);
                    float ox = MathF.Floor(x), fx = ox, ex = 1 - fx;
                    float oy = MathF.Floor(y), fy = oy, ey = 1 - fx;
                    float oz = MathF.Floor(z), fz = oz, ez = 1 - fx;
                    int ii = (int)ox + ky * (int)oy + kz * (int)oz;
                    FieldArr[ii] += ex * ey * ez;               // [(int)ox, (int)oy, (int)oz]
                    FieldArr[ii + 1] += fx * ey * ez;
                    FieldArr[ii + ky] += ex * fy * ez;
                    FieldArr[ii + 1 + ky] += fx * fy * ez;
                    FieldArr[ii + kz] += ex * ey * fz;
                    FieldArr[ii + 1 + kz] += fx * ey * fz;
                    FieldArr[ii + ky + kz] += ex * fy * fz;
                    FieldArr[ii + 1 + ky + kz] += fx * fy * fz;
                }
            }
            StageEnd("make3d");

            Stage("make3dvec");
            (FieldVec, FieldVecOld) = (FieldVecOld, FieldVec);
            // Array.Fill(FieldVec, Vector3.Zero);
            int w = 3;  // width

            // scan over all field points
            for (int z = 0; z < nz; z++) {
                int liz = Math.Max(z - w, 0), hiz = Math.Min(z + w, nz - 1);
                for (int y = 0; y < ny; y++) {
                    int liy = Math.Max(y - w, 0), hiy = Math.Min(y + w, ny - 1);
                    for (int x = 0; x < nx; x++) {
                        int lix = Math.Max(x - w, 0), hix = Math.Min(x + w, nx - 1);

                        // for each field point, scan over neighbours and create vector field
                        Vector3 vec = Vector3.Zero;
                        for (int dz = liz; dz <= hiz; dz++) {
                            for (int dy = liy; dy <= hiy; dy++) {
                                for (int dx = lix; dx <= hix; dx++) {
                                    Vector3 dir = new(dx - x, dy - y, dz - z);
                                    float k = (float)dir.Length();
                                    if (k < w && k != 0) {
                                        float ff = FieldArr[dx + dy * ky + dz * kz];
                                        dir *= ff / k * k;
                                        vec += dir;
                                    }
                                }
                            }
                        }
                        FieldVec[x + y * ky + z * kz] = vec;
                    }
                }
            }
            StageEnd("make3dvec");

            Stage("make3dtwist");
            // scan over all field points
            for (int z = 0; z < nz; z++) {
                for (int y = 0; y < ny; y++) {
                    for (int x = 0; x < nx; x++) {
                        int ii = x + y * ky + z * kz;
                        FieldTwist[ii] = Vector3.Cross(FieldVec[ii], FieldVecOld[ii]);
                    }
                }
            }
            StageEnd("make3dtwist");

            Stage("reduce3dtwist");
            Array.Fill(FieldTwistSc, Vector3.Zero);
            for (int z = 0; z < nz; z++) {
                int sz = z / sc * sckz;
                for (int y = 0; y < ny; y++) {
                    int sy = y / sc * scky;
                    for (int x = 0; x < nx; x++) {
                        int sx = x / sc;
                        int ii = x + y * ky + z * kz;
                        int scii = sz + sy + sx;
                        FieldTwistSc[scii] += FieldTwist[ii];
                    }
                }
            }
            StageEnd("reduce3dtwist");

            /**
({log,exp, tan,PI}=Math)
tand = d => tan(d*PI/180)
lz = 500
hz = 8000
nz = 20
llz = log(lz)
lhz = log(hz)
ldz = lhz-llz

rz = nz/ldz
f = z => (log(z)-llz) * rz
f(500)
f(8000)

fi = iz => exp(iz / rz + llz)
wf = iz => fi(iz+1) - fi(iz)

wf(f(500))
wf(f(1000))
wf(f(2000))

wf(0)
wf(1)
wf(18)
wf(19)

tand(35) * lz * 2
tand(35) * hz * 2
nx = 30
tand(70/nx)

                    */
        }


    }
}
