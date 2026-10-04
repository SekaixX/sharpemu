// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Network;
using Xunit;

namespace SharpEmu.Libs.Tests.Network;

public sealed class HttpConnectionTests
{
    private const ulong MemoryBase = 0x1_0000_0000;
    private const ulong UserAgentAddress = MemoryBase + 0x100;
    private const ulong ServerNameAddress = MemoryBase + 0x200;
    private const ulong SchemeAddress = MemoryBase + 0x300;
    private const int InvalidId = unchecked((int)0x80431100);
    private const int InvalidValue = unchecked((int)0x804311FE);

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void CreateConnectionExportRegistersForBothGenerations(Generation generation)
    {
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(generation));

        Assert.True(manager.TryGetExport("Kiwv9r4IZCc", out var export));
        Assert.Equal("sceHttpCreateConnection", export.Name);
        Assert.Equal("libSceHttp", export.LibraryName);
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void CreateConnectionReturnsLogicalHandleWithoutOpeningHostNetwork(Generation generation)
    {
        var memory = new FakeCpuMemory(MemoryBase, 0x1000);
        memory.WriteCString(UserAgentAddress, "SharpEmu test");
        memory.WriteCString(ServerNameAddress, "telemetry.example.invalid");
        memory.WriteCString(SchemeAddress, "https");
        var context = new CpuContext(memory, generation);
        var contextId = CreateHttpContext(context);

        try
        {
            var templateId = CreateTemplate(context, contextId);
            context[CpuRegister.Rdi] = unchecked((ulong)templateId);
            context[CpuRegister.Rsi] = ServerNameAddress;
            context[CpuRegister.Rdx] = SchemeAddress;
            context[CpuRegister.Rcx] = 443;
            context[CpuRegister.R8] = 1;

            Assert.Equal(0, HttpExports.HttpCreateConnection(context));
            Assert.InRange(unchecked((int)context[CpuRegister.Rax]), 1, int.MaxValue);

            context[CpuRegister.Rdi] = unchecked((ulong)templateId);
            Assert.Equal(0, HttpExports.HttpDeleteTemplate(context));
            context[CpuRegister.Rsi] = ServerNameAddress;
            context[CpuRegister.Rdx] = SchemeAddress;
            context[CpuRegister.Rcx] = 443;
            context[CpuRegister.R8] = 1;
            Assert.Equal(InvalidId, HttpExports.HttpCreateConnection(context));
        }
        finally
        {
            context[CpuRegister.Rdi] = unchecked((ulong)contextId);
            Assert.Equal(0, HttpExports.HttpTerm(context));
        }
    }

    [Fact]
    public void CreateConnectionValidatesTemplateAndGuestStrings()
    {
        var memory = new FakeCpuMemory(MemoryBase, 0x1000);
        memory.WriteCString(UserAgentAddress, "SharpEmu test");
        memory.WriteCString(ServerNameAddress, "host.example.invalid");
        memory.WriteCString(SchemeAddress, "https");
        var context = new CpuContext(memory, Generation.Gen5);
        var contextId = CreateHttpContext(context);

        try
        {
            var templateId = CreateTemplate(context, contextId);

            context[CpuRegister.Rdi] = unchecked((ulong)(templateId + 1));
            context[CpuRegister.Rsi] = ServerNameAddress;
            context[CpuRegister.Rdx] = SchemeAddress;
            Assert.Equal(InvalidId, HttpExports.HttpCreateConnection(context));

            context[CpuRegister.Rdi] = unchecked((ulong)templateId);
            context[CpuRegister.Rsi] = 0;
            Assert.Equal(InvalidValue, HttpExports.HttpCreateConnection(context));

            context[CpuRegister.Rsi] = ServerNameAddress;
            context[CpuRegister.Rdx] = 0;
            Assert.Equal(InvalidValue, HttpExports.HttpCreateConnection(context));

            Span<byte> unterminated = stackalloc byte[8];
            unterminated.Fill((byte)'x');
            var truncatedAddress = MemoryBase + 0x1000 - (ulong)unterminated.Length;
            Assert.True(memory.TryWrite(truncatedAddress, unterminated));
            context[CpuRegister.Rsi] = truncatedAddress;
            context[CpuRegister.Rdx] = SchemeAddress;
            Assert.Equal(
                (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT,
                HttpExports.HttpCreateConnection(context));
        }
        finally
        {
            context[CpuRegister.Rdi] = unchecked((ulong)contextId);
            Assert.Equal(0, HttpExports.HttpTerm(context));
        }
    }

    private static int CreateHttpContext(CpuContext context)
    {
        context[CpuRegister.Rdi] = 1;
        context[CpuRegister.Rsi] = 2;
        context[CpuRegister.Rdx] = 0x4000;
        Assert.Equal(0, HttpExports.HttpInit(context));
        return unchecked((int)context[CpuRegister.Rax]);
    }

    private static int CreateTemplate(CpuContext context, int contextId)
    {
        context[CpuRegister.Rdi] = unchecked((ulong)contextId);
        context[CpuRegister.Rsi] = UserAgentAddress;
        context[CpuRegister.Rdx] = 1;
        context[CpuRegister.Rcx] = 0;
        Assert.Equal(0, HttpExports.HttpCreateTemplate(context));
        return unchecked((int)context[CpuRegister.Rax]);
    }
}
