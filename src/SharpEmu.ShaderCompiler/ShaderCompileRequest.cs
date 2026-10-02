// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Collections.Generic;
using System.Linq;
using SharpEmu.ShaderCompiler.Resources;

namespace SharpEmu.ShaderCompiler;

// One fixed-function vertex attribute a fetch instruction reads instead of its buffer.
public sealed record ShaderVertexInput(
    uint Pc,
    uint Location,
    uint FetchComponentCount,
    uint ComponentCount,
    uint NumberFormat,
    uint DestinationSelect,
    bool PerInstance,
    IReadOnlyList<uint> AliasPcs,
    uint FormatComponentCount = 0)
{
    public ShaderVertexInput(
        uint pc,
        uint location,
        uint componentCount,
        uint numberFormat,
        bool perInstance,
        IReadOnlyList<uint> aliasPcs)
        : this(
            pc,
            location,
            componentCount,
            componentCount,
            numberFormat,
            componentCount switch
            {
                1u => 4u,
                2u => 4u | (5u << 3),
                3u => 4u | (5u << 3) | (6u << 6),
                4u => 4u | (5u << 3) | (6u << 6) | (7u << 9),
                _ => 0u,
            },
            perInstance,
            aliasPcs,
            0)
    {
    }

    // The fixed-function input only needs the highest source component selected
    // by the guest descriptor. Zero format components retain the upstream direct mapping.
    public uint InputComponentCount
    {
        get
        {
            if (FormatComponentCount is < 1 or > 4)
            {
                return ComponentCount;
            }

            var required = 0u;
            for (uint destination = 0; destination < FetchComponentCount; destination++)
            {
                var source = ResolveSourceComponent(destination);
                if (source >= 0)
                {
                    required = Math.Max(required, (uint)source + 1);
                }
            }

            // Keep one declared attribute even when every destination is a constant.
            return Math.Max(required, 1u);
        }
    }

    // Non-negative values name a source component; -1/-2 are zero/one constants.
    // -3 marks a reserved selector or an invalid format description.
    public int ResolveSourceComponent(uint destination)
    {
        if (FormatComponentCount is < 1 or > 4)
        {
            return destination < ComponentCount ? (int)destination : -1;
        }

        var selector = (DestinationSelect >> (int)(destination * 3)) & 0x7u;
        return selector switch
        {
            0 => -1,
            1 => -2,
            >= 4 and <= 7 => (int)((selector - 4) % FormatComponentCount),
            _ => -3,
        };
    }
}

public readonly record struct ShaderClipSpaceTransform(
    bool Enabled,
    float ScaleX,
    float ScaleY,
    float OffsetX,
    float OffsetY,
    float HalfExtentX,
    float HalfExtentY);

// Static geometry-assembly parameters for a merged ES+GS program lowered to a
// Vulkan mesh shader. Primitive type values follow the guest VGT primitive enum:
// point-list=1, line-list=2, triangle-list=4 and triangle-strip=6.
public sealed record ShaderMeshInfo(
    uint InputPrimitive,
    uint PrimitivesPerGroup,
    uint VerticesPerGroup,
    uint MaxVertices,
    uint MaxPrimitives,
    uint OutputPrimitive,
    uint ProvokingVertex)
{
    public const uint DrawDwordCount = 6;

    public uint InputPrimitiveSize => InputPrimitive switch
    {
        1 => 1,
        2 => 2,
        _ => 3,
    };

    public uint InputPrimitiveStep => InputPrimitive == 6
        ? 1
        : InputPrimitiveSize;

    public uint InputPrimitiveCount(uint vertices)
    {
        var size = InputPrimitiveSize;
        return vertices < size
            ? 0
            : (vertices - size) / InputPrimitiveStep + 1;
    }

    public uint InputVertexCount(uint primitives) => primitives == 0
        ? 0
        : (primitives - 1) * InputPrimitiveStep + InputPrimitiveSize;
}

// Everything an emitter needs to compile one permutation of a program: the decoded
// program, its resource plan applied to one specialization, and the binding layout.
public sealed class ShaderCompileRequest
{
    public const uint UnboundedThreadCount = uint.MaxValue;
    public const int WrittenRangeDwordCount = 3;
    public bool TraceDeviceAddressFaults { get; init; }
    public bool TraceMeshOutputs { get; init; }

    public ShaderCompileRequest(ShaderResourcePlan plan, SpecializedResourceInfo resources, BindingLayout bindings)
    {
        Program = plan.Graph.Program;
        Stage = plan.Stage;
        Hash = plan.Hash;
        Memory = plan.Memory;
        Resources = resources;
        Bindings = bindings;
        UserDataBase = plan.UserDataBase;
        UserDataCount = plan.UserDataCount;
        UsesFlattenedTable = RequiresFlattenedTable(plan, resources);
        UsesGlobalDataShare = BindingLayout.UsesGlobalDataShare(Program);
        ReadsShaderBase = BindingLayout.ReadsShaderBase(Program);
        FixedLaneWaveSize = plan.Graph.WaveSize;
        FixedLaneReads = new Dictionary<uint, FixedLaneReadBinding>(
            plan.Graph.FixedLaneReads);
        FixedLaneWrites = new Dictionary<uint, FixedLaneWriteBinding>(
            plan.Graph.FixedLaneWrites);

        FlattenedSlotByMemoryIndex = new Dictionary<int, uint>(plan.FlattenedSlotByMemoryIndex);
        IndirectKeyMemoryIndices = plan.IndirectImages.Select(access => access.Key.MemoryIndex).ToHashSet();
        IndirectOffsetKeyMemoryIndices = plan.IndirectImages.Where(access => access.KeyIsAddressOffset)
            .Select(access => access.Key.MemoryIndex).ToHashSet();
        IndirectRootByMemoryIndex = plan.IndirectImages.ToDictionary(access => access.MemoryIndex, access => access.Key.MemoryIndex);

        var writtenSlots = new Dictionary<int, uint>();
        foreach (var range in plan.DeviceAddressRanges)
        {
            if (!range.Written || !plan.WrittenRangeSlotByHandle.TryGetValue(range.Handle, out var slot))
            {
                continue;
            }

            foreach (var memoryIndex in range.MemoryIndices)
            {
                writtenSlots[memoryIndex] = slot;
            }
        }

        WrittenRangeSlotByMemoryIndex = writtenSlots;
    }

    // The flattened table is bound when host reads, written ranges or indirect mappings fill it.
    public static bool RequiresFlattenedTable(ShaderResourcePlan plan, SpecializedResourceInfo resources) =>
        plan.TableReads.Count != 0 || plan.WrittenRangeCount != 0 ||
        resources.Info.Images.Any(image => image.IndirectSearchIterations != 0);

    public Gen5ShaderProgram Program { get; }
    public ShaderStage Stage { get; }
    public ulong Hash { get; }
    public MemoryAccessTable Memory { get; }
    public SpecializedResourceInfo Resources { get; }
    public BindingLayout Bindings { get; }
    public uint UserDataBase { get; }
    public uint UserDataCount { get; }
    public bool UsesFlattenedTable { get; }
    public bool UsesGlobalDataShare { get; }
    public bool ReadsShaderBase { get; }
    public uint FixedLaneWaveSize { get; }
    public IReadOnlyDictionary<uint, FixedLaneReadBinding> FixedLaneReads { get; }
    public IReadOnlyDictionary<uint, FixedLaneWriteBinding> FixedLaneWrites { get; }

    // Host-flattened scalar reads: memory index → flattened table slot.
    public IReadOnlyDictionary<int, uint> FlattenedSlotByMemoryIndex { get; }

    // Scalar reads whose loaded dword selects an indirect image at a later instruction.
    public IReadOnlySet<int> IndirectKeyMemoryIndices { get; }

    public IReadOnlySet<int> IndirectOffsetKeyMemoryIndices { get; }

    // Indirect image accesses: memory index → the memory index of the key read.
    public IReadOnlyDictionary<int, int> IndirectRootByMemoryIndex { get; }

    // Written device-address accesses: memory index → the flattened slot of their range.
    public IReadOnlyDictionary<int, uint> WrittenRangeSlotByMemoryIndex { get; }

    public uint WaveSize { get; init; } = 32;
    public uint HostSubgroupSize { get; init; } = 64;
    public uint ScratchDwords { get; init; }
    public uint LocalDataShareDwords { get; init; }
    public bool EnableGraphicsSubgroupOperations { get; init; } = true;
    public bool BufferInt64AtomicsSupported { get; init; }
    public bool ShaderFloat64Supported { get; init; }
    public bool ShaderSignedZeroInfNanPreserveFloat32Supported { get; init; }
    public Gen5ComputeSystemRegisters? ComputeSystemRegisters { get; init; }
    public ShaderMeshInfo? Mesh { get; init; }

    public IReadOnlyList<Gen5PixelOutputBinding> PixelOutputs { get; init; } = [];
    public uint PixelInputEnable { get; init; }
    public uint PixelCustomInterpolationMask { get; init; }
    public uint PixelInputAddress { get; init; }
    public IReadOnlyList<uint>? PixelInputCntl { get; init; }
    public bool PixelEarlyDepth { get; init; }

    public int RequiredVertexOutputCount { get; init; }
    public IReadOnlyList<ShaderVertexInput> VertexInputs { get; init; } = [];
    public uint PositionExportControl { get; init; }
    public ShaderClipSpaceTransform ClipSpace { get; init; }

    public uint LocalSizeX { get; init; } = 1;
    public uint LocalSizeY { get; init; } = 1;
    public uint LocalSizeZ { get; init; } = 1;

    // Fixed bounds for standalone modules; runtime-limit layouts read the dispatch data.
    public uint ThreadCountX { get; init; } = UnboundedThreadCount;
    public uint ThreadCountY { get; init; } = UnboundedThreadCount;
    public uint ThreadCountZ { get; init; } = UnboundedThreadCount;
}
