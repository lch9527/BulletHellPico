using System.Collections.Generic;
using UnityEngine;

namespace HandFleet
{
    // Pooled local-space strokes stay attached to the imported nozzle mouths.
    public sealed class KestrelExhaust
    {
        enum Bank { Pitch, Yaw, Roll, Forward, Back }
        struct Port { public Bank Bank; public LineRenderer Line; public int Sign; public float Scale; }
        readonly List<Port> ports = new List<Port>(32);
        public bool Reduced;

        public KestrelExhaust(Transform arena, Material material)
        {
            foreach (var socket in arena.GetComponentsInChildren<Transform>(true))
            {
                if (!socket.name.StartsWith("FX_", System.StringComparison.Ordinal)) continue;
                var fields = socket.name.Split('_');
                if (fields.Length < 3 || !System.Enum.TryParse(fields[1], out Bank bank)) continue;
                var line = FleetGeometry.Line(socket, "Kestrel exhaust", material, .003f, 7);
                line.endWidth = 0;
                line.enabled = false;
                var sign = bank == Bank.Pitch ? (socket.name.EndsWith("_Upper") ? 1 : -1) :
                    (fields[2] == "L" ? 1 : -1);
                ports.Add(new Port { Bank = bank, Line = line, Sign = sign,
                    Scale = 1f / Mathf.Max(.0001f, Mathf.Abs(socket.lossyScale.z)) });
            }
        }

        public void Apply(Vector3 offset, Vector3 turn)
        {
            foreach (var p in ports)
            {
                // Handedness and the second fusion hull can both mirror the ship's lateral axis.
                int mirror = p.Line.transform.localToWorldMatrix.determinant < 0 ? -1 : 1;
                float strength;
                switch (p.Bank)
                {
                    case Bank.Pitch: strength = p.Sign * (turn.x / 25f - offset.y / .05f); break;
                    case Bank.Yaw: strength = p.Sign * mirror * (turn.y / 25f + offset.x / .05f); break;
                    case Bank.Roll: strength = p.Sign * mirror * turn.z / 30f; break;
                    case Bank.Forward: strength = offset.z / .055f; break;
                    default: strength = -offset.z / .055f; break;
                }
                p.Line.enabled = strength > .025f;
                if (!p.Line.enabled) continue;
                float length = (.004f + .052f * Mathf.Clamp01(strength)) * (Reduced ? .6f : 1);
                for (int i = 0; i < 7; i++)
                {
                    float t = i / 6f;
                    var point = Vector3.forward * t;
                    if (p.Bank == Bank.Roll)
                        point += t * .32f * new Vector3(Mathf.Sin(t*5f), p.Sign*(1-Mathf.Cos(t*5f)), 0);
                    if (p.Bank == Bank.Pitch) point += Vector3.up * (.23f*t*t);
                    if (p.Bank == Bank.Yaw) point *= .65f + .20f*Mathf.Sin(Time.time*18f);
                    p.Line.SetPosition(i, point * (length * p.Scale));
                }
            }
        }
    }
}
