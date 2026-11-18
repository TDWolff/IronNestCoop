using Il2Cpp;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// Diagnostic (M5): while the traverse wheel is being dragged — locally or by a remote player — log the wheel's and
/// turret's internal values 5× per second. Comparing a local drag (turret turns) with a remote drag (turret doesn't)
/// shows which value TurretController actually follows. All names from the dump (build 26352746).
/// Only runs with DebugLog on and ProbeTurret on.
/// </summary>
internal static class TurretProbe
{
    static float next;
    static bool wasActive;

    public static void Update(bool remoteDragging)
    {
        if (!Mod.DebugLog.Value || !Mod.ProbeTurret.Value) return;
        var t = Registry.All<TurretController>().FirstOrDefault();
        var d = t?.rotationDial;
        if (d == null) return;

        bool local = d.IsDragging;
        bool active = local || remoteDragging || t.rotationDialDragActive;
        if (!active)
        {
            if (wasActive) { Log(t, d, "end"); wasActive = false; }
            return;
        }
        if (!wasActive) { Log(t, d, local ? "LOCAL start" : "REMOTE start"); wasActive = true; next = Time.unscaledTime + 0.2f; return; }
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.2f;
        Log(t, d, local ? "local" : "remote");
    }

    static void Log(TurretController t, DialInteractable d, string tag)
    {
        Mod.Log.Msg($"PROBE {tag,-12} dial acc {d.AccumulatedValue,10:0.##} angle {d.currentRotationAngle,8:0.##} lastAngle {d.lastAngle,8:0.##} " +
                    $"speed {d.MeasuredRotationSpeed,8:0.##} norm {d.NormalizedRotationSpeed,6:0.###} drag {d.IsDragging} | " +
                    $"turret dragActive {t.rotationDialDragActive} base {t.rotationDialBaseAngle,10:0.##} desired {t.DesiredRotation,8:0.##} " +
                    $"angle {t.CurrentAngle,8:0.##} vel {t.rotationVelocity,7:0.##} desVel {t.desiredRotationVelocity,7:0.##} " +
                    $"desVelTarget {t.desiredRotationVelocityTarget,7:0.##} speedDial {t.isUsingSpeedDial}");
    }
}
