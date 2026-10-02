// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Diagnostics;
using SharpEmu.ShaderCompiler.Resources;
using SharpEmu.ShaderCompiler.Tests.Resources;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.ShaderCompiler.Tests;

public sealed class Gen5InterpolationParameterTests
{
    [Theory]
    [InlineData(0u, false, 1u, true)]
    [InlineData(1u, false, 2u, true)]
    [InlineData(0u, true, 1u, false)]
    [InlineData(1u, true, 2u, false)]
    public void ParameterMove_SelectsVertexAndPreservesCustomData(
        uint selector, bool custom, uint vertex, bool subtractOrigin)
    {
        var request = Request(selector, custom);
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(request, out var shader, out var error), error);
        var instructions = Instructions(shader.Spirv);
        Assert.Contains(instructions, instruction => instruction.Opcode == SpirvOp.Capability &&
            instruction.Operands[0] == (uint)SpirvCapability.FragmentBarycentricKhr);
        var input = Assert.Single(instructions, instruction => instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[1] == (uint)SpirvDecoration.PerVertexKhr).Operands[0];
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[0] == input && instruction.Operands[1] == (uint)SpirvDecoration.Flat);
        var firstAccess = instructions.First(instruction => instruction.Opcode == SpirvOp.AccessChain &&
            instruction.Operands[2] == input);
        Assert.Contains(instructions, instruction => instruction.Opcode == SpirvOp.Constant &&
            instruction.Operands[1] == firstAccess.Operands[3] && instruction.Operands[2] == vertex);
        Assert.Equal(subtractOrigin, instructions.Any(instruction => instruction.Opcode == SpirvOp.FSub));
        ValidateWhenAvailable(shader.Spirv);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void FlatP0Move_UsesFixedFunctionFlatOnlyWithoutCustomInterpolation(
        bool custom,
        bool fixedFunctionFlat)
    {
        var request = Request(2, custom);
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(request, out var shader, out var error), error);
        var instructions = Instructions(shader.Spirv);
        Assert.Equal(!fixedFunctionFlat, instructions.Any(instruction =>
            instruction.Opcode == SpirvOp.Capability &&
            instruction.Operands[0] == (uint)SpirvCapability.FragmentBarycentricKhr));
        Assert.Equal(!fixedFunctionFlat, instructions.Any(instruction =>
            instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[1] == (uint)SpirvDecoration.PerVertexKhr));
        Assert.Equal(fixedFunctionFlat, instructions.Any(instruction =>
            instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[1] == (uint)SpirvDecoration.Flat));
        var input = Assert.Single(instructions, instruction => instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[1] == (uint)SpirvDecoration.Location).Operands[0];
        Assert.Contains(instructions, instruction => instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[0] == input && instruction.Operands[1] == (uint)SpirvDecoration.Location);
        ValidateWhenAvailable(shader.Spirv);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonFlatP0Move_StillUsesPerVertexInput(bool custom)
    {
        var request = Request(2, custom, flat: false);
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(request, out var shader, out var error), error);
        var instructions = Instructions(shader.Spirv);
        Assert.Contains(instructions, instruction => instruction.Opcode == SpirvOp.Capability &&
            instruction.Operands[0] == (uint)SpirvCapability.FragmentBarycentricKhr);
        Assert.Contains(instructions, instruction => instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[1] == (uint)SpirvDecoration.PerVertexKhr);
        ValidateWhenAvailable(shader.Spirv);
    }

    [Theory]
    [InlineData(3u)]
    [InlineData(255u)]
    public void ReservedSelector_Fails(uint selector)
    {
        Assert.False(Gen5SpirvTranslator.TryCompileProgram(Request(selector, false), out _, out var error));
        Assert.Contains("reserved interpolation parameter selector", error);
    }

    [Fact]
    public void MixedBarycentricLocations_ShareBuiltIns()
    {
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(Request(0, true, 0x77), out var shader, out var error), error);
        var builtIns = Instructions(shader.Spirv)
            .Where(instruction => instruction.Opcode == SpirvOp.Decorate &&
                instruction.Operands[1] == (uint)SpirvDecoration.BuiltIn)
            .Select(instruction => instruction.Operands[2]).ToArray();
        Assert.Equal(builtIns.Length, builtIns.Distinct().Count());
        Assert.Contains((uint)SpirvBuiltIn.BaryCoordKhr, builtIns);
        Assert.Contains((uint)SpirvBuiltIn.BaryCoordNoPerspKhr, builtIns);
        Assert.Contains((uint)SpirvBuiltIn.SampleId, builtIns);
        ValidateWhenAvailable(shader.Spirv);
    }

    [Fact]
    public void OrdinaryInterpolation_DoesNotRequireBarycentricFeature()
    {
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(
            Request(0, false, opcode: "VInterpP2F32"), out var shader, out var error), error);
        Assert.DoesNotContain(Instructions(shader.Spirv), instruction => instruction.Opcode == SpirvOp.Capability &&
            instruction.Operands[0] == (uint)SpirvCapability.FragmentBarycentricKhr);
        ValidateWhenAvailable(shader.Spirv);
    }

    [Fact]
    public void LinearInterpolation_DecoratesOrdinaryPixelInputNoPerspective()
    {
        var request = Request(0, false, opcode: "VInterpP2F32", noPerspective: true, flat: false);
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(request, out var shader, out var error), error);
        var instructions = Instructions(shader.Spirv);
        var input = Assert.Single(instructions, instruction => instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[1] == (uint)SpirvDecoration.NoPerspective).Operands[0];
        Assert.Contains(instructions, instruction => instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[0] == input && instruction.Operands[1] == (uint)SpirvDecoration.Location);
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[0] == input && instruction.Operands[1] == (uint)SpirvDecoration.Flat);
        ValidateWhenAvailable(shader.Spirv);
    }

    [Fact]
    public void FlatInterpolation_TakesPrecedenceOverNoPerspective()
    {
        var request = Request(0, false, opcode: "VInterpP2F32", noPerspective: true);
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(request, out var shader, out var error), error);
        var instructions = Instructions(shader.Spirv);
        var input = Assert.Single(instructions, instruction => instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[1] == (uint)SpirvDecoration.Flat).Operands[0];
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == SpirvOp.Decorate &&
            instruction.Operands[0] == input && instruction.Operands[1] == (uint)SpirvDecoration.NoPerspective);
        ValidateWhenAvailable(shader.Spirv);
    }

    [Fact]
    public void PullModel_DoesNotSilentlyUseZeroCoordinates()
    {
        Assert.False(Gen5SpirvTranslator.TryCompileProgram(Request(0, true, 8), out _, out var error));
        Assert.Contains("Pull-model interpolation", error);
    }

    private static ShaderCompileRequest Request(
        uint selector, bool custom, uint inputs = 2, string opcode = "VInterpMovF32",
        bool noPerspective = false, bool flat = true)
    {
        var interpolation = new Gen5ShaderInstruction(0, Gen5ShaderEncoding.Vintrp, opcode,
            [selector], [Gen5Operand.Vector(selector)], [Gen5Operand.Vector(4)], new Gen5InterpolationControl(1, 2));
        var program = ResourceTestProgram.Program(interpolation, ResourceTestProgram.EndProgram(4));
        var (plan, resources, layout) = ResourceTestProgram.Prepare(program, ShaderStage.Pixel, userDataCount: 0);
        return new ShaderCompileRequest(plan, resources, layout)
        {
            PixelInputAddress = inputs | (noPerspective ? 0x20u : 0u),
            PixelInputEnable = inputs | (noPerspective ? 0x20u : 0u),
            PixelInputCntl = [0, flat ? 0x401u : 1u],
            PixelCustomInterpolationMask = custom ? 2u : 0u,
        };
    }

    private static List<Instruction> Instructions(byte[] code)
    {
        var words = new uint[code.Length / 4];
        Buffer.BlockCopy(code, 0, words, 0, code.Length);
        var result = new List<Instruction>();
        for (var index = 5; index < words.Length;)
        {
            var count = (int)(words[index] >> 16);
            result.Add(new Instruction((SpirvOp)(words[index] & 0xFFFF), words[(index + 1)..(index + count)]));
            index += count;
        }
        return result;
    }

    private static void ValidateWhenAvailable(byte[] code)
    {
        var sdk = Environment.GetEnvironmentVariable("VULKAN_SDK");
        if (string.IsNullOrWhiteSpace(sdk))
        {
            return;
        }
        var executable = Path.Combine(sdk, OperatingSystem.IsWindows() ? "Bin/spirv-val.exe" : "bin/spirv-val");
        if (!File.Exists(executable))
        {
            return;
        }
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, code);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("--target-env");
            start.ArgumentList.Add("vulkan1.2");
            start.ArgumentList.Add(path);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed record Instruction(SpirvOp Opcode, uint[] Operands);
}
