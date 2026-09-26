using Relay.Sim;
using UnityEngine;

// Owns the sim clock and the only copy of the running state. Everything else reads it.
//
// Real time decides only *when* a tick runs, never *what* it computes: every tick is the same.
// Fixed 1/120 s step. So the frame rate can change how smooth the run looks, but never how it ends.
public sealed class SimRunner : MonoBehaviour
{
    const float DtSeconds = 1f / SimConstants.TicksPerSecond;

    // The most ticks one frame may run. 30 fps already needs 4 per frame, so a cap of 4 would put
    // 30 fps in permanent slow motion. 8 covers down to 15 fps; below that, accept slow motion
    // rather than let one long frame (a GC pause) trigger a catch-up that causes the next frame.
    const int MaxCatchUp = 8;

    [SerializeField] string machineId = "m002_chain";
    [SerializeField] int targetFrameRate = 60;
    [SerializeField] bool autoRun = true; // start on Play; after a reset, wait for Run

    MachineDef _machine;
    SimState _armed, _prev, _cur;
    float _acc;
    bool _running;
    bool _reported;

    // Read-only views. `ref readonly` means a caller can look without copying and cannot write.
    public ref readonly SimState Prev => ref _prev;
    public ref readonly SimState Cur => ref _cur;
    public float Alpha { get; private set; }
    public MachineDef Machine => _machine;
    public int TargetFrameRate => targetFrameRate;
    public bool Running => _running;

    // Same stopping rule as the harness's StepMany(m.Ticks): the tick budget, or a terminal phase.
    public bool Done =>
        _cur.Tick >= _machine.Ticks ||
        _cur.Phase == SimPhase.Settled ||
        _cur.Phase == SimPhase.Failed;

    void Awake()
    {
        QualitySettings.vSyncCount = 0; // with vsync on, targetFrameRate is ignored
        Application.targetFrameRate = targetFrameRate;

        // Resources, not File: on Android the machine files live inside the APK.
        var text = Resources.Load<TextAsset>($"Machines/{machineId}");
        if (text == null)
        {
            Debug.LogError(
                $"No machine Resources/Machines/{machineId}.json. Run tools/build-sim.ps1.");
            enabled = false;
            return;
        }

        _machine = MachineLoader.Parse(text.text, machineId);
        _armed = _machine.ToState();
        _prev = _armed;
        _cur = _armed;
        _running = autoRun;
    }

    public void Run()
    {
        if (enabled)
            _running = true;
    }

    // Back to the armed machine, waiting for Run. Not called Reset(): Unity treats a method with
    // that name as an editor message and calls it by itself when the component is added.
    //
    // A plain struct copy is enough because Step never writes into a state's arrays, it always
    // allocates new ones. `_armed`'s arrays are therefore exactly as they were at load.
    // Phase 1: player placements belong in _armed, so they survive this for free.
    public void ResetMachine()
    {
        _cur = _armed;
        _prev = _armed;
        _acc = 0f;
        _running = false; // or the next run inherits a partial tick
        _reported = false;
    }

    void Update()
    {
        if (_running && !Done)
        {
            _acc += Time.deltaTime; // the ONLY deltaTime in the project

            int steps = 0;
            while (_acc >= DtSeconds && steps < MaxCatchUp && !Done)
            {
                _prev = _cur;
                _cur = Simulation.Step(in _cur);
                _acc -= DtSeconds;
                steps++;
            }

            // Still behind after the cap: drop the backlog. The run slows down; it does not change.
            if (_acc >= DtSeconds)
                _acc = 0f;
        }

        if (!_running || Done)
        {
            _prev = _cur;
            _acc = 0f; // nothing to blend towards

            if (Done)
                Report();
        }

        Alpha = _acc / DtSeconds;
    }

    void Report()
    {
        if (_reported)
            return;

        _reported = true;

        Debug.Log(
            $"HASH {_machine.Id} {_cur.Phase} at tick {_cur.Tick} 0x{_cur.Hash():x8} " +
            $"targetFps={targetFrameRate}");
    }
}