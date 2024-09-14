using System;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.RenderCore;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace CUE4Parse.UE4.Assets.Exports.Animation;

public class FMorphTargetDelta
{
    public readonly FVector PositionDelta;
    public readonly FVector TangentZDelta;
    public readonly uint SourceIdx;

    public FMorphTargetDelta(FArchive Ar)
    {
        PositionDelta = Ar.Read<FVector>();
        if (Ar.Ver < EUnrealEngineObjectUE4Version.MORPHTARGET_CPU_TANGENTZDELTA_FORMATCHANGE)
        {
            TangentZDelta = (FVector) Ar.Read<FDeprecatedSerializedPackedNormal>();
        }
        else
        {
            TangentZDelta = Ar.Read<FVector>();
        }
        SourceIdx = Ar.Read<uint>();
    }

    public FMorphTargetDelta(FVector pos, FVector tan, uint index)
    {
        PositionDelta = pos;
        TangentZDelta = tan;
        SourceIdx = index;
    }
}

public class FMorphTargetLODModel
{
    /** vertex data for a single LOD morph mesh */
    public readonly FMorphTargetDelta[] Vertices;
    /** number of original verts in the base mesh */
    public readonly int NumBaseMeshVerts;
    /** list of sections this morph is used */
    public readonly int[] SectionIndices;
    /** Is this LOD generated by reduction setting */
    public readonly bool bGeneratedByEngine;
    /* The source filename use to import this morph target. If source is empty this morph target was import with the LOD geometry. */
    public readonly string? SourceFilename;

    public FMorphTargetLODModel()
    {
        Vertices = [];
        NumBaseMeshVerts = 0;
        SectionIndices = [];
        bGeneratedByEngine = false;
    }

    public FMorphTargetLODModel(FArchive Ar)
    {
        if (FEditorObjectVersion.Get(Ar) < FEditorObjectVersion.Type.AddedMorphTargetSectionIndices)
        {
            Vertices = Ar.ReadArray(() => new FMorphTargetDelta(Ar));
            NumBaseMeshVerts = Ar.Read<int>();
            bGeneratedByEngine = false;
        }
        else if (FFortniteMainBranchObjectVersion.Get(Ar) < FFortniteMainBranchObjectVersion.Type.SaveGeneratedMorphTargetByEngine)
        {
            Vertices = Ar.ReadArray(() => new FMorphTargetDelta(Ar));
            NumBaseMeshVerts = Ar.Read<int>();
            SectionIndices = Ar.ReadArray<int>();
            bGeneratedByEngine = false;
        }
        else
        {
            if (Ar.Game == EGame.GAME_TheCastingofFrankStone)
            {
                Ar.Position += 4; // NumVertices
                Vertices = [];
                SectionIndices = Ar.ReadArray<int>();
                bGeneratedByEngine = Ar.ReadBoolean();
                return;
            }

            var bVerticesAreStrippedForCookedBuilds = false;
            if (FUE5PrivateFrostyStreamObjectVersion.Get(Ar) >= FUE5PrivateFrostyStreamObjectVersion.Type.StripMorphTargetSourceDataForCookedBuilds)
            {
                // Strip source morph data for cooked build if targets don't include mobile. Mobile uses CPU morphing which needs the source morph data.
                bVerticesAreStrippedForCookedBuilds = Ar.ReadBoolean();
            }

            if (bVerticesAreStrippedForCookedBuilds)
            {
                Ar.Position += 4; // NumVertices
                Vertices = [];
            }
            else
            {
                Vertices = Ar.ReadArray(() => new FMorphTargetDelta(Ar));
            }

            NumBaseMeshVerts = Ar.Read<int>();
            SectionIndices = Ar.ReadArray<int>();
            bGeneratedByEngine = Ar.ReadBoolean();
        }

        if (FFortniteMainBranchObjectVersion.Get(Ar) >= FFortniteMainBranchObjectVersion.Type.MorphTargetCustomImport)
        {
            SourceFilename = Ar.ReadFString();
        }
    }

    public FMorphTargetLODModel(FMorphTargetVertexInfoBuffers buffer, int index, int[] sectionIndices)
    {
        var batchStartOffsetPerMorph = buffer.BatchStartOffsetPerMorph[index];
        var batchesPerMorph = buffer.BatchesPerMorph[index];
        var posPrecision = buffer.PositionPrecision;
        var tanPrecision = buffer.TangentZPrecision;

        var size = 0;
        for (var j = 0; j < batchesPerMorph; j++)
        {
            var batch = buffer.MorphData[batchStartOffsetPerMorph + j];
            size += (int)batch.NumElements;
        }

        Vertices = new FMorphTargetDelta[size];
        NumBaseMeshVerts = size;
        SectionIndices = sectionIndices;
        bGeneratedByEngine = false;

        var k = 0;
        for (var j = 0; j < batchesPerMorph; j++)
        {
            var batch = buffer.MorphData[batchStartOffsetPerMorph + j];
            var posMin = batch.PositionMin;
            var tanMin = batch.TangentZMin;

            foreach (var delta in batch.QuantizedDelta)
            {
                var pos = new FVector((posMin.X + delta.Position.X) * posPrecision,
                    (posMin.Y + delta.Position.Y) * posPrecision, (posMin.Z + delta.Position.Z) * posPrecision);
                var tan = new FVector((tanMin.X + delta.TangentZ.X) * tanPrecision,
                    (tanMin.Y + delta.TangentZ.Y) * tanPrecision, (tanMin.Z + delta.TangentZ.Z) * tanPrecision);
                Vertices[k++] = new FMorphTargetDelta(pos, tan, delta.Index);
            }
        }
    }
}

public class UMorphTarget : UObject
{
    public FMorphTargetLODModel[] MorphLODModels = { new() };

    public override void Deserialize(FAssetArchive Ar, long validPos)
    {
        base.Deserialize(Ar, validPos);

        if (!Ar.Versions["MorphTarget"])
        {
            Ar.Position = validPos;
            return;
        }

        var stripData = Ar.Read<FStripDataFlags>();
        if (!stripData.IsDataStrippedForServer())
        {
            MorphLODModels = Ar.ReadArray(() => new FMorphTargetLODModel(Ar));
        }
    }

    protected internal override void WriteJson(JsonWriter writer, JsonSerializer serializer)
    {
        base.WriteJson(writer, serializer);

        writer.WritePropertyName(nameof(MorphLODModels));
        serializer.Serialize(writer, MorphLODModels);
    }
}
