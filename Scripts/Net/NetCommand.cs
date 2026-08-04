namespace SandlotSlugfest.Net;

/// <summary>
/// Everything one player can do that changes the game.
///
/// An online game is kept in step by exchanging decisions rather than world state. Both machines
/// run the same simulation from the same seed, so as long as they apply the same decisions in the
/// same order they stay identical down to the last random draw — which is why this list has to be
/// exhaustive. Anything a human can do that consumes a random number or mutates the situation
/// belongs here; anything left out is a desync waiting to happen.
/// </summary>
public enum NetVerb
{
    /// <summary>A pitch is on its way: type, aim, and whether the signature move was spent.</summary>
    Pitch,

    /// <summary>The bat came through. Carries when, where, and how.</summary>
    Swing,

    /// <summary>He let it go by.</summary>
    Take,

    /// <summary>The runner is going.</summary>
    Steal,

    /// <summary>A throw to a base.</summary>
    ThrowOver,

    /// <summary>A bat off the bench for the man due up.</summary>
    PinchHit,

    /// <summary>A call to the bullpen.</summary>
    ChangePitcher,

    /// <summary>Put him on.</summary>
    IntentionalWalk,

    /// <summary>The pitch timer ran out. An automatic ball, and it has to be agreed like any other.</summary>
    PitchClock,

    /// <summary>End-of-inning agreement check. Carries a hash of the situation, not an action.</summary>
    Checksum,
}

/// <summary>
/// One decision, in the order the host says it happened.
///
/// The payload is deliberately four numbers and nothing else. A command has to be cheap enough to
/// send several times a second and simple enough that both machines cannot possibly disagree about
/// what it means.
/// </summary>
public readonly struct NetCommand
{
    /// <summary>Position in the authoritative order. Commands are applied strictly in sequence.</summary>
    public readonly int Sequence;

    public readonly NetVerb Verb;

    /// <summary>Whichever peer acted. Used only for the log and for rejecting impostors.</summary>
    public readonly bool FromAway;

    public readonly float A, B, C, D;

    public NetCommand(int sequence, NetVerb verb, bool fromAway, float a, float b, float c, float d)
    {
        Sequence = sequence;
        Verb = verb;
        FromAway = fromAway;
        A = a; B = b; C = c; D = d;
    }

    /// <summary>Packed for the wire. Godot marshals a float array across C# RPC without fuss.</summary>
    public float[] Pack() => new[]
    {
        Sequence, (float)(int)Verb, FromAway ? 1f : 0f, A, B, C, D,
    };

    public static NetCommand Unpack(float[] v) => v is not { Length: 7 }
        ? default
        : new NetCommand((int)v[0], (NetVerb)(int)v[1], v[2] > 0.5f, v[3], v[4], v[5], v[6]);

    public override string ToString() => $"#{Sequence} {Verb} {(FromAway ? "away" : "home")}";
}
