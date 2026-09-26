using Relay.Sim;
using UnityEngine;

// Owns the sim clock and the only copy of the running state. Everything else reads it.
// Real time decides only *when* a tick runs, never *what* it computes: every tick is the same
// fixed 1/120 s step. So the frame rate can change how smooth the run looks, but never how it
// ends.
public sealed class SimRunner : MonoBehaviour
{
    const float DtSeconds = 1f / SimConstants.TicksPerSecond;

    // The most ticks one frame may run. 30 fps in permanent slow motion, 8 covers down to 15 fps;
    // below that, permanent slow motion rather than let one long frame (a GC pause) trigger a
    // catch-up that causes the next.
    const int MaxCatchUp = 8;

    [SerializeField] string machineId = "m002_chain";
    [SerializeField] int targetFrameRate = 60;

    MachineDef _machine;
    SimState _armed, _prev, _cur;
    float _acc;
    bool _reported;

    // Read-only views. `ref readonly` means a caller can look without copying and cannot write.
    public ref readonly SimState Prev => ref _prev;
    public ref readonly SimState Cur => ref _cur;
    public float Alpha { get; private set; }
    public MachineDef Machine => _machine;
    public int TargetFrameRate => targetFrameRate;

    // Same stopping rule as the harness's StepMany(m.Ticks): the tick budget, or a terminal phase.
    public bool Done =>
        _cur.Tick >= _machine.Ticks
        || _cur.Phase == SimPhase.Settled
        || _cur.Phase == SimPhase.Failed;

    void Awake()
    {
        QualitySettings.vSyncCount = 0;       // with vsync on, targetFrameRate is ignored
        Application.targetFrameRate = targetFrameRate;

        // Resources, not File: on Android the machine files live inside the APK.
        var text = Resources.Load<TextAsset>("Machines/" + machineId);
        if (text == null)
        {
            Debug.LogError($"No machine Resources/Machines/{machineId}.json. Run tools/build-sim.ps1.");
            enabled = false;
            return;
        }

        _machine = MachineLoader.Parse(text.text, machineId);
        _armed = _machine.ToState();
        _prev = _armed;
        _cur = _armed;
    }

    void Update()
    {
        if (!Done)
        {
            _acc += Time.deltaTime;          // the ONLY deltaTime in the project

            int steps = 0;
            while (_acc >= DtSeconds && steps < MaxCatchUp && !Done)
            {
                _prev = _cur;
                _cur = Simulation.Step(in _cur);
                _acc -= DtSeconds;
                steps++;
            }

            // Still behind after the cap, drop the backlog. The run slows down; it does not change.
            if (_acc >= DtSeconds) _acc = 0f;
        }

        if (Done)
        {
            _prev = _cur;
            _acc = 0f;                       // nothing left to blend towards
            Report();
        }

        Alpha = _acc / DtSeconds;
    }

    void Report()
    {
        if (_reported) return;
        _reported = true;

        Debug.Log(
            $"HASH {_machine.Id} {_cur.Phase} @ tick {_cur.Tick} hash {_cur.Hash():X16} " +
            $"targetFps={targetFrameRate}"
        );
    }
}