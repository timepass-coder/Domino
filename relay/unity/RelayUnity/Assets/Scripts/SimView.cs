using FixMath;
using Relay.Sim;
using UnityEngine;

// Read-only presentation. It is handed the two latest states and a blend factor, draws them, and
// keeps nothing. Floats live here and only here: the sim never sees a number this class computes.
[RequireComponent(typeof(SimRunner))]
public sealed class SimView : MonoBehaviour
{
    const float SurfaceThickness = 0.1f;
    const float Depth = 0.5f;

    SimRunner _runner;
    Transform[] _balls;
    Transform[] _dominoes;
    float _fps;
    int _hashTick = -1;
    ulong _hash;

    void Start()
    {
        _runner = GetComponent<SimRunner>();
        if (!_runner.enabled) { enabled = false; return; }

        ref readonly SimState s = ref _runner.Cur;

        // Every primitive is made here, once. No Instantiate or Destroy while a run is going.
        foreach (var surface in s.Surfaces) MakeSurface(surface);

        _balls = new Transform[s.Balls.Length];
        for (int i = 0; i < _balls.Length; i++)
        {
            float d = s.Balls[i].Radius.Float * 2f;
            _balls[i] = Make(
                PrimitiveType.Sphere,
                "ball " + i,
                new Vector3(d, d, d)
            );
        }

        _dominoes = new Transform[s.Dominoes.Length];
        for (int i = 0; i < _dominoes.Length; i++)
        {
            var dom = s.Dominoes[i];
            _dominoes[i] = Make(
                PrimitiveType.Cube,
                "domino " + i,
                new Vector3(dom.Thickness.Float, dom.Height.Float, Depth)
            );
        }

        FrameCamera(s.Bounds);
        DrawIn(in s, in s, 1f);
    }

    // LateUpdate runs after every Update, so the runner has already stepped this frame.
    void LateUpdate()
    {
        DrawIn(in _runner.Prev, in _runner.Cur, _runner.Alpha);
        _fps = Mathf.Lerp(
            _fps,
            1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f),
            0.05f
        );
    }

    void DrawIn(in SimState prev, in SimState cur, float alpha)
    {
        for (int i = 0; i < _balls.Length; i++)
        {
            Vector2 p = Vector2.Lerp(
                ToVector(prev.Balls[i].Pos),
                ToVector(cur.Balls[i].Pos),
                alpha
            );

            _balls[i].localPosition = p;
        }

        for (int i = 0; i < _dominoes.Length; i++)
        {
            var dom = cur.Dominoes[i];

            float theta = Mathf.Lerp(
                prev.Dominoes[i].Theta.Float,
                dom.Theta.Float,
                alpha
            );

            DominoPose(
                ToVector(dom.Base),
                dom.Height.Float,
                dom.Thickness.Float,
                theta,
                out Vector2 centre,
                out float degrees
            );

            _dominoes[i].localPosition = centre;
            _dominoes[i].localRotation = Quaternion.Euler(0f, 0f, degrees);
        }
    }

    // Where the cube's centre goes, and how far it turns, for a domino leaning by theta.
    // Theta > 0 leans right about the right base corner; theta < 0 leans about the left one.
    // Upright, the centre sits (h/2) above the base. Rotating the centre about the pivot by -theta
    // (clockwise for a lean to the right) puts the leading top corner exactly where the sim's
    // LeadingCornerX says it should.
    public static void DominoPose(
        Vector2 basePos,
        float height,
        float thickness,
        float theta,
        out Vector2 centre,
        out float degrees)
    {
        float side = theta >= 0f ? 1f : -1f;

        var pivot = new Vector2(
            basePos.x + side * thickness * 0.5f,
            basePos.y
        );

        var fromPivot = new Vector2(
            -side * thickness * 0.5f,
            height * 0.5f
        );

        float c = Mathf.Cos(-theta);
        float s = Mathf.Sin(-theta);

        centre = pivot + new Vector2(
            fromPivot.x * c - fromPivot.y * s,
            fromPivot.x * s + fromPivot.y * c
        );

        degrees = -theta * Mathf.Rad2Deg;
    }

    void MakeSurface(Surface surface)
    {
        Vector2 a = ToVector(surface.A);
        Vector2 b = ToVector(surface.B);
        Vector2 mid = (a + b) * 0.5f;

        Transform t;

        if (surface.IsHorizontal)
        {
            // Balls roll on the line itself, so the slab hangs below it.
            t = Make(
                PrimitiveType.Cube,
                "surface " + surface.Id,
                new Vector3(Mathf.Abs(b.x - a.x), SurfaceThickness, Depth)
            );

            t.localPosition = new Vector2(
                mid.x,
                mid.y - SurfaceThickness * 0.5f
            );
        }
        else
        {
            t = Make(
                PrimitiveType.Cube,
                "surface " + surface.Id,
                new Vector3(
                    SurfaceThickness,
                    Mathf.Abs(b.y - a.y),
                    Depth
                )
            );

            t.localPosition = mid;
        }
    }

    Transform Make(PrimitiveType type, string name, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;

        Destroy(go.GetComponent<Collider>()); // Unity physics plays no part; don't leave it lying around

        go.transform.SetParent(transform, false);
        go.transform.localScale = scale;

        return go.transform;
    }

    static void FrameCamera(WorldBounds bounds)
    {
        var cam = Camera.main;
        if (cam == null) return;

        float x0 = bounds.X0.Float;
        float x1 = bounds.X1.Float;
        float y0 = bounds.Y0.Float;
        float y1 = bounds.Y1.Float;

        cam.orthographic = true;
        cam.transform.SetPositionAndRotation(
            new Vector3(
                (x0 + x1) * 0.5f,
                (y0 + y1) * 0.5f,
                -10f
            ),
            Quaternion.identity
        );

        cam.orthographicSize =
            Mathf.Max(
                (y1 - y0) * 0.5f,
                (x1 - x0) * 0.5f / cam.aspect
            ) * 1.05f;
    }

    static Vector2 ToVector(F64Vec2 v) =>
        new Vector2(v.X.Float, v.Y.Float);

    void OnGUI()
    {
        ref readonly SimState s = ref _runner.Cur;

        if (s.Tick != _hashTick)
        {
            _hash = s.Hash();
            _hashTick = s.Tick;
        }

        GUI.skin.label.fontSize = Mathf.Max(14, Screen.height / 45);

        GUILayout.Label($"runner={_runner.Machine.Id}");
        GUILayout.Label($"hash {_hash:X16} tick {s.Tick} phase {s.Phase}");
        GUILayout.Label($"fps {_fps:F0} target {_runner.TargetFrameRate}");
    }
}