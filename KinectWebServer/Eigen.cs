using MathNet.Numerics.LinearAlgebra;
using static Kinect2WebServer.Program;

namespace Kinect2WebServer {

    partial class Program {
        public static (double[], double[,]) Eigen(Statsxyz ss) {
            // Create a 3x3 matrix
            var matrix = Matrix<double>.Build.DenseOfArray(new double[,]
            {
    { ss.xx, ss.xy, ss.zx },
    { ss.xy, ss.yy, ss.yz },
    { ss.zx, ss.yz, ss.zz }
            });

        // Get the eigenvalues and eigenvectors of the matrix
        var eigen = matrix.Evd();
        return (eigen.EigenValues.Real().ToArray(), eigen.EigenVectors.ToArray());
    }

    public static (double[], double[,]) Eigenxy(Statsxyz ss) {
        // Create a 3x3 matrix
        var matrix = Matrix<double>.Build.DenseOfArray(new double[,]
        {
    { ss.xx, ss.xy},
    { ss.xy, ss.yy}
        });

    // Get the eigenvalues and eigenvectors of the matrix
    var eigen = matrix.Evd();
    return (eigen.EigenValues.Real().ToArray(), eigen.EigenVectors.ToArray());
    }
}  // Program

}  // namespace
