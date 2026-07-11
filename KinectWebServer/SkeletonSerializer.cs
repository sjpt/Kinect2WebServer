using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Kinect;
namespace Kinect2WebServer {
    /// <summary>
    /// Serializes a Kinect skeleton to JSON fromat.
    /// </summary>
    public static class SkeletonSerializer {
        [DataContract]
        class JSONSkeletonCollection {
            [DataMember(Name = "skeletons")]
            public List<JSONSkeleton> Skeletons { get; set; }

            [DataMember(Name = "time")]
            public double Time { get; set; }
        }

        [DataContract]
        class JSONSkeleton {
            [DataMember(Name = "id")]
            public string ID { get; set; }

            [DataMember(Name = "joints")]
            public List<JSONJoint> Joints { get; set; }
            [DataMember(Name = "handRightState")]
            public HandState HandRightState { get; internal set; }
            [DataMember(Name = "handLeftState")]
            public HandState HandLeftState { get; internal set; }

            public TrackingConfidence HandRightConfidence { get; internal set; }
            [DataMember(Name = "handLeftConfidence")]
            public TrackingConfidence HandLeftConfidence { get; internal set; }

        }

        [DataContract]
        class JSONJoint {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "x")]
            public double X { get; set; }

            [DataMember(Name = "y")]
            public double Y { get; set; }

            [DataMember(Name = "z")]
            public double Z { get; set; }

            [DataMember(Name = "trackingState")]
            public int TrackingState { get; set; }

        }

        /// <summary>
        /// Serializes an array of Kinect skeletons into an array of JSON skeletons.
        /// </summary>
        /// <param name="skeletons">The Kinect skeletons.</param>
        /// <param name="mode">Mode (color or depth).</param>
        /// <returns>A JSON representation of the skeletons.</returns>
        public static string Serialize(this List<Body> skeletons, double secs) {
            JSONSkeletonCollection jsonSkeletons = new JSONSkeletonCollection { Skeletons = new() };
            jsonSkeletons.Time = secs;

            foreach (var skeleton in skeletons) {
                JSONSkeleton jsonSkeleton = new JSONSkeleton {
                    ID = skeleton.TrackingId.ToString(),
                    Joints = new List<JSONJoint>()
                };

                foreach (Joint joint in skeleton.Joints.Values) {
                    var point = joint.Position;

                    jsonSkeleton.Joints.Add(new JSONJoint {
                        Name = joint.JointType.ToString().ToLower(),
                        X = point.X, // + Program.camx, use toGeometry if we are going to use skeletons
                        Y = point.Y, // + Program.camy,
                        Z = -point.Z, // + Program.camz,
                        TrackingState = (int)joint.TrackingState
                    });
                }
                jsonSkeleton.HandRightConfidence = skeleton.HandRightConfidence;
                jsonSkeleton.HandLeftConfidence = skeleton.HandLeftConfidence;
                jsonSkeleton.HandRightState = skeleton.HandRightState;
                jsonSkeleton.HandLeftState = skeleton.HandLeftState;

                jsonSkeletons.Skeletons.Add(jsonSkeleton);
            }

            return Serialize(jsonSkeletons);
        }

        /// <summary>
        /// Serializes an object to JSON.
        /// </summary>
        /// <param name="obj">The specified object.</param>
        /// <returns>A JSON representation of the object.</returns>
        private static string Serialize(object obj) {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(obj.GetType());

            MemoryStream ms = new();
            serializer.WriteObject(ms, obj);
            ms.Dispose();
            return Encoding.Default.GetString(ms.ToArray());
        }
    }
}
