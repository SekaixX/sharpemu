// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Network;
using Xunit;

namespace SharpEmu.Libs.Tests.Network;

public sealed class RudpExportsTests : IDisposable
{
    private readonly CpuContext _ctx = new(new FakeCpuMemory(0x1_0000_0000, 0x1000), Generation.Gen5);

    public RudpExportsTests() => RudpExports.ResetForTests();

    public void Dispose() => RudpExports.ResetForTests();

    [Fact]
    public void PollCreateReturnsDistinctPositiveIdsAndRetainsCapacity()
    {
        _ctx[CpuRegister.Rdi] = 64;
        Assert.Equal(1, RudpExports.RudpPollCreate(_ctx));
        Assert.Equal(1UL, _ctx[CpuRegister.Rax]);
        Assert.True(RudpExports.TryGetPollCapacity(1, out var firstCapacity));
        Assert.Equal(64U, firstCapacity);

        _ctx[CpuRegister.Rdi] = 8;
        Assert.Equal(2, RudpExports.RudpPollCreate(_ctx));
        Assert.Equal(2UL, _ctx[CpuRegister.Rax]);
        Assert.True(RudpExports.TryGetPollCapacity(2, out var secondCapacity));
        Assert.Equal(8U, secondCapacity);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(0x1_0000_0000UL)]
    public void PollCreateRejectsInvalidEventCounts(ulong requestedEvents)
    {
        _ctx[CpuRegister.Rdi] = requestedEvents;

        Assert.Equal(
            (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT,
            RudpExports.RudpPollCreate(_ctx));
        Assert.Equal(
            unchecked((ulong)(int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT),
            _ctx[CpuRegister.Rax]);
    }
}
