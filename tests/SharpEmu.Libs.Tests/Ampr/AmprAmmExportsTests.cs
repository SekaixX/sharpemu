// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Ampr;
using SharpEmu.Libs.Kernel;
using SharpEmu.Libs.Tests.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Ampr;

[Collection(KernelMemoryCompatStateCollection.Name)]
public sealed class AmprAmmExportsTests
{
    private const ulong GuestMemoryBase = 0x1_0000_0000;
    private const ulong OutputAddress = GuestMemoryBase + 0x100;

    [Fact]
    public void GetVirtualAddressRangesReportsReservedWindowAndEmptyMultimapRange()
    {
        var context = new CpuContext(new FakeCpuMemory(GuestMemoryBase, 0x1000), Generation.Gen5);
        context[CpuRegister.Rdi] = GuestMemoryBase + 0x100;
        context[CpuRegister.Rsi] = GuestMemoryBase + 0x108;
        context[CpuRegister.Rdx] = GuestMemoryBase + 0x110;
        context[CpuRegister.Rcx] = GuestMemoryBase + 0x118;

        Assert.Equal(0, AmprExports.AmmGetVirtualAddressRanges(context));
        Assert.True(context.TryReadUInt64(GuestMemoryBase + 0x100, out var vaStart));
        Assert.True(context.TryReadUInt64(GuestMemoryBase + 0x108, out var vaEnd));
        Assert.True(context.TryReadUInt64(GuestMemoryBase + 0x110, out var multimapStart));
        Assert.True(context.TryReadUInt64(GuestMemoryBase + 0x118, out var multimapEnd));
        Assert.Equal(GuestMemoryLayout.GuestUserAddressStart, vaStart);
        Assert.Equal(GuestMemoryLayout.GuestPrimaryUserAddressLimit, vaEnd);
        Assert.Equal(vaEnd, multimapStart);
        Assert.Equal(vaEnd, multimapEnd);
    }

    [Fact]
    public void GiveDirectMemoryUsesAmmBlockRulesAndSharedKernelAllocator()
    {
        const ulong size = 0x20_0000;
        var searchEnd = GuestMemoryLayout.DirectBytes;
        var searchStart = searchEnd - (16UL * size);
        var context = new CpuContext(new FakeCpuMemory(GuestMemoryBase, 0x1000), Generation.Gen5);
        ulong allocation = 0;

        try
        {
            context[CpuRegister.Rdi] = searchStart;
            context[CpuRegister.Rsi] = searchEnd;
            context[CpuRegister.Rdx] = size;
            context[CpuRegister.Rcx] = 0;
            context[CpuRegister.R8] = 1;
            context[CpuRegister.R9] = OutputAddress;

            Assert.Equal(0, AmprExports.AmmGiveDirectMemory(context));
            Assert.True(context.TryReadUInt64(OutputAddress, out allocation));
            Assert.InRange(allocation, searchStart, searchEnd - size);
            Assert.Equal(0UL, allocation & (size - 1));
        }
        finally
        {
            if (allocation != 0)
            {
                context[CpuRegister.Rdi] = allocation;
                context[CpuRegister.Rsi] = size;
                Assert.Equal(0, KernelMemoryCompatExports.KernelReleaseDirectMemory(context));
            }
        }
    }

    [Theory]
    [InlineData(0x4000UL, 0UL, 0)]
    [InlineData(0x20_0000UL, 0x4000UL, 0)]
    [InlineData(0x20_0000UL, 0UL, 2)]
    public void GiveDirectMemoryRejectsNonAmmBlockArguments(ulong size, ulong alignment, int usage)
    {
        var context = new CpuContext(new FakeCpuMemory(GuestMemoryBase, 0x1000), Generation.Gen5);
        context[CpuRegister.Rdi] = 0;
        context[CpuRegister.Rsi] = GuestMemoryLayout.DirectBytes;
        context[CpuRegister.Rdx] = size;
        context[CpuRegister.Rcx] = alignment;
        context[CpuRegister.R8] = unchecked((ulong)(long)usage);
        context[CpuRegister.R9] = OutputAddress;

        Assert.Equal((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT,
            AmprExports.AmmGiveDirectMemory(context));
    }
}
