// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Np;
using Xunit;

namespace SharpEmu.Libs.Tests.Np;

public sealed class NpEntitlementAccessExportsTests
{
    [Fact]
    public void GetEntitlementKey_MatchesOfflineNoEntitlementContract()
    {
        const ulong memoryBase = 0x1_0000_0000;
        const ulong labelAddress = memoryBase + 0x100;
        const ulong keyAddress = memoryBase + 0x200;
        var memory = new FakeCpuMemory(memoryBase, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);

        ctx[CpuRegister.Rdi] = 0;
        ctx[CpuRegister.Rsi] = labelAddress;
        ctx[CpuRegister.Rdx] = keyAddress;
        Assert.Equal(
            unchecked((int)0x817D0007),
            NpEntitlementAccessExports.NpEntitlementAccessGetEntitlementKey(ctx));

        ctx[CpuRegister.Rsi] = 0;
        Assert.Equal(
            unchecked((int)0x817D0002),
            NpEntitlementAccessExports.NpEntitlementAccessGetEntitlementKey(ctx));
    }

    [Fact]
    public void GetEntitlementKey_RegistersWithCatalogIdentity()
    {
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));

        Assert.True(manager.TryGetExport("5LiMEPuW0DQ", out var export));
        Assert.Equal("sceNpEntitlementAccessGetEntitlementKey", export.Name);
        Assert.Equal("libSceNpEntitlementAccess", export.LibraryName);
    }
}
