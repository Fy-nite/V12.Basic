using System;

namespace V12.Basic.Player;

public enum MovementEnvelopeStage
{
    Idle,
    Attack,
    Decay,
    Sustain,
    Release
}

/// <summary>
/// ADSR shape for movement speed. The envelope scales the player's input speed
/// over time: attack ramps to full speed, decay settles to the sustain level,
/// and release bleeds the speed away after the stick or keys are released. It
/// gives a heavy character a little inertia without changing collision response.
/// </summary>
public sealed class MovementEnvelope
{
    public float Attack { get; set; } = 0.22f;
    public float Decay { get; set; } = 0.18f;

    /// <summary>Normalized held speed, 0..1.</summary>
    public float Sustain { get; set; } = 0.75f;

    public float Release { get; set; } = 0.28f;

    public float Value { get; private set; }
    public MovementEnvelopeStage Stage { get; private set; } = MovementEnvelopeStage.Idle;

    public void Reset()
    {
        Value = 0f;
        Stage = MovementEnvelopeStage.Idle;
    }

    public void Update(float deltaTime, bool active)
    {
        float dt = MathF.Max(0f, deltaTime);
        float sustain = Math.Clamp(Sustain, 0f, 1f);

        if (active)
        {
            if (Stage is MovementEnvelopeStage.Idle or MovementEnvelopeStage.Release)
                Stage = MovementEnvelopeStage.Attack;

            switch (Stage)
            {
                case MovementEnvelopeStage.Attack:
                    if (Attack <= 0.0001f)
                    {
                        Value = 1f;
                        Stage = MovementEnvelopeStage.Decay;
                    }
                    else
                    {
                        Value = MoveTowards(Value, 1f, dt / Attack);
                        if (Value >= 0.999f)
                        {
                            Value = 1f;
                            Stage = MovementEnvelopeStage.Decay;
                        }
                    }
                    break;

                case MovementEnvelopeStage.Decay:
                    if (Decay <= 0.0001f)
                    {
                        Value = sustain;
                        Stage = MovementEnvelopeStage.Sustain;
                    }
                    else
                    {
                        float rate = MathF.Max(0.0001f, (1f - sustain) / Decay);
                        Value = MoveTowards(Value, sustain, rate * dt);
                        if (MathF.Abs(Value - sustain) < 0.001f)
                        {
                            Value = sustain;
                            Stage = MovementEnvelopeStage.Sustain;
                        }
                    }
                    break;

                case MovementEnvelopeStage.Sustain:
                    Value = sustain;
                    break;
            }

            return;
        }

        if (Stage == MovementEnvelopeStage.Idle && Value <= 0.0001f)
        {
            Value = 0f;
            return;
        }

        Stage = MovementEnvelopeStage.Release;
        if (Release <= 0.0001f)
        {
            Value = 0f;
            Stage = MovementEnvelopeStage.Idle;
            return;
        }

        Value = MoveTowards(Value, 0f, dt / Release);
        if (Value <= 0.0001f)
        {
            Value = 0f;
            Stage = MovementEnvelopeStage.Idle;
        }
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        return current < target
            ? MathF.Min(current + maxDelta, target)
            : MathF.Max(current - maxDelta, target);
    }
}
