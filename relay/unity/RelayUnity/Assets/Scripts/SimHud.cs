using System.Collections;
using System.Diagnostics;
using Relay.Sim;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Debug HUD: the run's numbers, Run / Reset buttons, and the reset latency the player actually
// sees. Keep it until Phase 4. It is the only place input reaches the runner.
[RequireComponent(typeof(SimRunner))]
public sealed class SimHud : MonoBehaviour
{
    const double BudgetMs = 500.0;

    SimRunner _runner;
    readonly Stopwatch _clock = new Stopwatch(); // outside the sim, so a Stopwatch is fine
    bool _measuring;
    double _lastMs = -1.0, _worstMs;
    int _resets;
    float _fps;
    int _hashTick = -1;
    ulong _hash;

    void Awake() => _runner = GetComponent<SimRunner>();

    void Update() => _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);

    void OnGUI()
    {
        if (!_runner.enabled) return;

        ref readonly SimState s = ref _runner.Cur;
        if (s.Tick != _hashTick) { _hash = s.Hash(); _hashTick = s.Tick; } // OnGUI runs several times a frame

        int size = Mathf.Max(14, Screen.height / 45);
        GUI.skin.label.fontSize = size;
        GUI.skin.button.fontSize = size;
        var buttonHeight = GUILayout.Height(size * 2.5f);

        GUILayout.Label($"Machine {_runner.Machine.Id}  tick {s.Tick}  {s.Phase}");
        GUILayout.Label($"$hash 0x{_hash:x}");
        GUILayout.Label($"fps {_fps:F0} (target {_runner.TargetFrameRate})");

        GUILayout.BeginHorizontal();
        GUI.enabled = !_runner.Running && !_runner.Done;
        if (GUILayout.Button("Run", buttonHeight)) _runner.Run();
        GUI.enabled = true;
        if (GUILayout.Button("Reset", buttonHeight)) OnResetPressed();
        GUILayout.EndHorizontal();

        if (_resets > 0)
        {
            string verdict = _worstMs < BudgetMs ? "under" : "OVER";
            GUILayout.Label(
                $"reset {_lastMs:F1} ms  worst {_worstMs:F1} ms  ({_resets}x, {verdict} ({BudgetMs:F0} ms))");
        }
    }

    void OnResetPressed()
    {
        if (_measuring) return; // a second tap before the first one showed

        _measuring = true;
        _clock.Restart();
        _runner.ResetMachine();
        StartCoroutine(StopClockOnceShown(Time.frameCount));
    }

    // The frame that was on its way to the screen when the tap arrived still shows the old run.
    // The first frame showing the armed machine is the next one, so stop at the end of that.
    IEnumerator StopClockOnceShown(int tapFrame)
    {
        while (Time.frameCount <= tapFrame)
            yield return new WaitForEndOfFrame();

        _clock.Stop();
        _lastMs = _clock.Elapsed.TotalMilliseconds;

        if (_lastMs > _worstMs)
            _worstMs = _lastMs;

        _resets++;
        _measuring = false;

        Debug.Log($"RESET {_lastMs:F2} ms (worst {_worstMs:F2} ms over {_resets})");
    }
}