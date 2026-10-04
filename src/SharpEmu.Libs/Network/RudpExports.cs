// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.Network;

/// <summary>
/// Minimal reliable-UDP poll object support. SharpEmu does not expose a host
/// RUDP transport yet, but callers still require a distinct non-zero poll ID
/// for their offline/event-processing path.
/// </summary>
public static class RudpExports
{
    private const int MaxLivePolls = 4096;
    private static readonly object PollGate = new();
    private static readonly Dictionary<int, uint> PollCapacities = [];
    private static int _nextPollId;

    [SysAbiExport(
        Nid = "MVbmLASjn5M",
        ExportName = "sceRudpPollCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceRudp")]
    public static int RudpPollCreate(CpuContext ctx)
    {
        // ABI: int sceRudpPollCreate(uint32_t maxEvents). The return value is
        // the poll ID itself, not an ORBIS_OK status. Returning a success stub
        // (zero) therefore leaves middleware with an invalid poll object.
        var requestedEvents = ctx[CpuRegister.Rdi];
        if (requestedEvents is 0 or > uint.MaxValue)
        {
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        lock (PollGate)
        {
            if (PollCapacities.Count >= MaxLivePolls)
            {
                return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_TRY_AGAIN);
            }

            int pollId;
            do
            {
                _nextPollId = _nextPollId == int.MaxValue ? 1 : _nextPollId + 1;
                pollId = _nextPollId;
            }
            while (PollCapacities.ContainsKey(pollId));

            PollCapacities.Add(pollId, unchecked((uint)requestedEvents));
            return ctx.SetReturn(pollId);
        }
    }

    public static void ResetRuntimeState()
    {
        lock (PollGate)
        {
            PollCapacities.Clear();
            _nextPollId = 0;
        }
    }

    internal static bool TryGetPollCapacity(int pollId, out uint capacity)
    {
        lock (PollGate)
        {
            return PollCapacities.TryGetValue(pollId, out capacity);
        }
    }

    internal static void ResetForTests() => ResetRuntimeState();
}
