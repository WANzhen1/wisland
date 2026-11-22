namespace WislandSharp.Core;

public sealed class Spring
{
    private const float PositionEpsilon = 0.0005f;
    private const float VelocityEpsilon = 0.001f;

    public float Value { get; private set; }
    public float Velocity { get; private set; }
    public float Target { get; private set; }

    public bool Rest =>
        Math.Abs(Velocity) < VelocityEpsilon &&
        Math.Abs(Value - Target) < PositionEpsilon;

    public Spring(float value = 0)
    {
        Value = value;
        Target = value;
    }

    public void Snap(float value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    public void To(float target) => Target = target;

    public void Step(float dt, float stiffness = 220f, float damping = 28f)
    {
        dt = Math.Clamp(dt, 0f, 0.05f);

        // 低帧率时拆成固定子步，避免单步过冲造成结尾抖动。
        int steps = Math.Max(1, (int)MathF.Ceiling(dt * 120f));
        float step = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            float accel = (Target - Value) * stiffness - Velocity * damping;
            Velocity += accel * step;
            Value += Velocity * step;
        }

        if (Math.Abs(Value - Target) < PositionEpsilon &&
            Math.Abs(Velocity) < VelocityEpsilon)
        {
            Value = Target;
            Velocity = 0;
        }
    }
}

public static class Ease
{
    public static float Out(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return 1f - (1f - t) * (1f - t) * (1f - t);
    }

    public static float InOut(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f;
    }
}
