using System;

namespace DutyRecap.Core;

public static class Grade
{
    private static readonly string[] CleanSnark =
    {
        "Noted. Nobody died, which is its own reward.",
        "A clean run. Suspiciously clean.",
        "Flawless. The mentor seal of approval.",
    };

    private static readonly string[] RoughSnark =
    {
        "Somebody call a mentor. Or do not.",
        "We all saw it.",
        "That is going in the minutes.",
        "Objection overruled.",
        "The table remembers.",
    };

    private static readonly string[] WipeSnark =
    {
        "Cursed run. You did this.",
        "The boss sends its regards.",
        "At least the queue popped.",
        "A learning experience, they called it.",
    };

    public static string Compute(RunRecap run)
    {
        if (run.Completed && run.Wipes == 0 && run.TotalDeaths == 0)
            return "S";

        if (run.Completed && run.TotalDeaths <= 2 && run.Wipes <= 1)
            return "A";

        if (run.Wipes <= 2)
            return "B";

        if (run.Wipes <= 4)
            return "C";

        return "F";
    }

    public static string SnarkFor(RunRecap run)
    {
        string[] pool;
        if (run.Wipes == 0 && run.TotalDeaths == 0)
            pool = CleanSnark;
        else if (run.Wipes == 0)
            pool = RoughSnark;
        else
            pool = WipeSnark;

        return pool[Random.Shared.Next(pool.Length)];
    }
}
