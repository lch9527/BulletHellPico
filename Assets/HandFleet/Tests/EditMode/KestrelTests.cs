using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HandFleet.Tests
{
    public class KestrelTests
    {
        [Test]
        public void GripCommandLightsSocketBeforeMeshFollowsAndReleaseClearsIt()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HandFleet/Prefabs/Kestrel.prefab");
            var hull = Object.Instantiate(prefab);
            var settings = ScriptableObject.CreateInstance<HandFleetSettings>();
            try
            {
                var sockets = hull.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("FX_")).ToArray();
                Assert.AreEqual(16, sockets.Length);
                Assert.AreEqual(5, sockets.Select(t => t.parent.name).Distinct().Count());
                var exhaust = new KestrelExhaust(hull.transform, AssetDatabase.LoadAssetAtPath<Material>("Assets/HandFleet/Materials/Cyan.mat"));
                var motor = new FleetMotor();
                var input = new FleetInput { Right = new FleetHand { Valid = true, Palm = Pose.identity } };
                motor.Grip(input, 1);
                motor.NozzleCommand += exhaust.Apply;
                Vector3 poseAtCommand = Vector3.one;
                motor.NozzleCommand += (offset, turn) => poseAtCommand = motor.Position;
                input.Right.Palm = new Pose(Vector3.forward * .04f, Quaternion.identity);
                motor.Step(input, new FleetCalibration(), new FleetFusion(), settings, .01f);
                Assert.AreEqual(Vector3.zero, poseAtCommand);
                Assert.That(motor.Position.z, Is.EqualTo(.04f).Within(1e-5f), "The held ship sits exactly on the palm.");
                var lines = hull.GetComponentsInChildren<LineRenderer>();
                Assert.AreEqual(2, lines.Count(line => line.enabled));
                Assert.IsTrue(lines.Where(line => line.enabled).All(line => line.transform.parent.name.StartsWith("FX_Forward_")));
                exhaust.Apply(-Vector3.forward, Vector3.one * 30);
                foreach (var group in new[] { "Back", "Pitch", "Yaw", "Roll" })
                    Assert.IsTrue(lines.Any(line => line.enabled && line.transform.parent.name.StartsWith("FX_" + group + "_")));
                motor.Release();
                Assert.IsFalse(lines.Any(line => line.enabled));
            }
            finally { Object.DestroyImmediate(hull); Object.DestroyImmediate(settings); }
        }
    }
}
