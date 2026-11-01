using System;
using System.Collections.Generic;
using System.Reflection;
using Mountbook.Views;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The one-frame request contract: a request set during a draw must be cleared before the next one.
/// </summary>
/// <remarks>
/// This exists because a flag added to <see cref="MountViewState"/> without a line in the reset opened a
/// browser tab every frame for the wiki action. Both view states are covered by reflection, so a new request
/// in either cannot silently leak - and <see cref="ConfigViewState"/> is linked into this project for exactly
/// that reason: its reset was missing a flag and nothing compiled or tested it.
/// <para>
/// Two lists carry the names reflection cannot infer. <see cref="PulseNames"/> holds the pulses whose names do
/// not end in "Requested" (a nullable id or a tri-state); <see cref="LatchNames"/> holds the plain flags that
/// are raised during a draw and mean "persist this once" rather than "do this once" - they are reset by
/// <c>ClearRequests</c> too, and nothing asserted it, so a latch left set would write the settings file every
/// frame.
/// </para>
/// </remarks>
public sealed class MountViewRequestTests
{
    /// <summary>One instance of each view state, so the same contract runs over both.</summary>
    public static TheoryData<object> States => new() { new MountViewState(), new ConfigViewState() };

    [Theory]
    [MemberData(nameof(States))]
    public void ClearRequestsResetsEveryRequestProperty(object state)
    {
        Type type = state.GetType();
        List<PropertyInfo> requests = RequestProperties(type);
        List<PropertyInfo> pulses = PulseProperties(type);

        Assert.NotEmpty(requests);

        foreach (PropertyInfo property in requests)
        {
            property.SetValue(state, RequestValue(property.PropertyType));
        }

        foreach (PropertyInfo pulse in pulses)
        {
            pulse.SetValue(state, NonNullValue(pulse.PropertyType));
        }

        ClearRequests(state);

        foreach (PropertyInfo property in requests)
        {
            object? value = property.GetValue(state);

            // A request is either a flag (true) or carries something to open (a URL); both must be back to
            // "nothing asked" - false, or null. Asserting only on `true` let a string request keep its value.
            Assert.True(
                value is null || value.Equals(false),
                $"{type.Name}.{property.Name} still holds '{value}' after ClearRequests.");
        }

        foreach (PropertyInfo pulse in pulses)
        {
            Assert.Null(pulse.GetValue(state));
        }
    }

    [Fact]
    public void ClearRequestsLeavesTheSelectionAlone()
    {
        var state = new MountViewState { SelectedId = 42 };

        state.ClearRequests();

        Assert.Equal(42u, state.SelectedId);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void ClearRequestsResetsEveryLatch(object state)
    {
        Type type = state.GetType();
        List<PropertyInfo> latches = LatchProperties(type);

        if (latches.Count == 0)
        {
            return;
        }

        foreach (PropertyInfo latch in latches)
        {
            latch.SetValue(state, true);
        }

        ClearRequests(state);

        foreach (PropertyInfo latch in latches)
        {
            Assert.False((bool)latch.GetValue(state)!);
        }
    }

    /// <summary>Every writable property whose name says it is a one-frame request.</summary>
    private static List<PropertyInfo> RequestProperties(Type type)
    {
        var found = new List<PropertyInfo>();

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanWrite && property.Name.EndsWith("Requested", StringComparison.Ordinal))
            {
                found.Add(property);
            }
        }

        return found;
    }

    /// <summary>The pulses whose names do not carry the "Requested" suffix.</summary>
    private static List<PropertyInfo> PulseProperties(Type type)
    {
        string[] names = PulseNames(type);
        var found = new List<PropertyInfo>(names.Length);

        foreach (string name in names)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    $"The pulse '{name}' is listed for {type.Name} but the property does not exist.");

            found.Add(property);
        }

        return found;
    }

    private static string[] PulseNames(Type type) =>
        type == typeof(MountViewState)
            ? ["ToggleWantedId", "ApplyViewIndex", "DeleteViewIndex"]
            : ["ThemePickedIndex", "FollowGameThemeSet"];

    /// <summary>
    /// The plain flags a draw raises and the window persists once - neither a "Requested" nor a pulse. Listed by
    /// hand for the same reason as <see cref="PulseNames"/>: nothing in the name says the reset owns them.
    /// </summary>
    private static string[] LatchNames(Type type) =>
        type == typeof(MountViewState) ? ["GrindChanged", "WatchChanged"] : [];

    /// <summary>Resolves <see cref="LatchNames"/> to properties, refusing a name that no longer exists.</summary>
    private static List<PropertyInfo> LatchProperties(Type type)
    {
        string[] names = LatchNames(type);
        var found = new List<PropertyInfo>(names.Length);

        foreach (string name in names)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    $"The latch '{name}' is listed for {type.Name} but the property does not exist.");

            found.Add(property);
        }

        return found;
    }

    /// <summary>A value a request can hold: <c>true</c> for a flag, a payload for a request that carries one.</summary>
    private static object RequestValue(Type type) => type == typeof(bool) ? true : "payload";

    private static object NonNullValue(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(uint))
        {
            return 1u;
        }

        if (underlying == typeof(bool))
        {
            return true;
        }

        return 1;
    }

    private static void ClearRequests(object state)
    {
        MethodInfo method = state.GetType().GetMethod("ClearRequests", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{state.GetType().Name} has no ClearRequests method.");

        method.Invoke(state, null);
    }
}
