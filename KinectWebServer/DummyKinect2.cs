#pragma warning disable IDE0040, IDE0049, CA1805, CA1304, CA1305, IDE0008, IDE0055, IDE0058, IDE0048, IDE0011

// This can be used if we don't have 'real' Kinect2 stuff around.
// Include this in the project and replace using 'Microsoft.Kinect' with 'using DummyMicrosoftKinect'
// Proably will never be needed, wasa temporaril;y needed while I couldn't find 64 but Microsoft.Kinect.dll
using System;
namespace DummyMicrosoftKinect2 {
    class Body {
        public bool IsTracked = false;
        public string Serialize() { return ""; }
    };
    class KinectSensor {
        public MultiSourceFrameReader OpenMultiSourceFrameReader(FrameSourceTypes k) { return null; }
        public void Open() { }
        public void Close() { }
        public void AcquireLatestFrame(MultiSourceFrameReader m) { }
        public bool IsOpen = false;
        public static KinectSensor GetDefault() { return null; }
        public bool IsAvailable = false;
        public event EventHandler<IsAvailableChangedEventArgs> IsAvailableChanged;
        public XFrameSource ColorFrameSource;
        public XFrameSource DepthFrameSource;
        public XFrameSource BodyFrameSource;
    };

    class XFrameSource {
        public XFrameDescription FrameDescription;
        public int BodyCount;
    }

    class XFrameDescription {
        public int Width, Height;
        public uint BytesPerPixel, LengthInPixels;
    }

    class XFrame {
        public void CopyFrameDataToArray(ushort[] x) { }
        public void CopyFrameDataToArray(byte[] x) { }
        public void Dispose() { }
        public TimeSpan RelativeTime;
        public void GetAndRefreshBodyData(Body[] bodies) { }
        public void CopyRawFrameDataToArray(byte[] b) { }

    }
    class XFrameReference {
        public TimeSpan RelativeTime;
        public XFrame AcquireFrame() { return null; }
    }
    class MultiSourceFrameReader {
        public void Dispose() { }
        public MultiSourceFrame AcquireLatestFrame() { return null; }
    };
    // class KinectFrame { }
    class MultiSourceFrame {
        public XFrameReference BodyFrameReference;
        public XFrameReference DepthFrameReference;
        public XFrameReference ColorFrameReference;
        public XFrameReference InfraredFrameReference;
        public XFrameReference BodyIndexFrameReference;
    };
    enum FrameSourceTypes { Depth, Body, Color, Infrared, BodyIndex }

    class IsAvailableChangedEventArgs { }
}
