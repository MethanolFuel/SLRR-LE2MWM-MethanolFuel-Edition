using System.Globalization;

namespace Slrr.Engine.Resources;

/// <summary>
/// Runtime resource ids (the ints scripts see from literals, <c>id()</c>, <c>getInfo(GII_TYPE)</c>):
/// <c>(globalArchiveIndex &lt;&lt; 16) | localId</c> for archive entries (measured, see <see cref="ArchiveTable"/>) and
/// <c>0xFFFF0000 | n</c> for run-time instances (measured: 0x536ed0 ORs 0xFFFF0000 into the allocated number).
/// 0 means "no resource".
/// </summary>
public static class ResourceIds
{
    public static int Make(int archive, int local) => unchecked((int)(((uint)archive << 16) | ((uint)local & 0xFFFF)));

    public static int Archive(int id) => (int)((uint)id >> 16);

    public static int Local(int id) => id & 0xFFFF;

    public static bool IsInstance(int id) => Archive(id) == ArchiveTable.RuntimeArchive;

    public static string Format(int id) => "0x" + ((uint)id).ToString("X8", CultureInfo.InvariantCulture);
}

/// <summary>
/// The <c>ResourceRef.RESTYPE_*</c> / <c>INSTANCE_*</c> / <c>RESOURCE_*</c> codes; they equal the rpk TypeOfEntry values
/// of the system.rpk type table (measured: <c>ResourceRef.class</c> constants vs <c>system.rpk</c> entries).
/// </summary>
public static class ResourceTypes
{
    public const int Invalid = 0;
    public const int InstanceGame = 1;
    public const int InstancePhysics = 2;
    public const int InstanceRender = 3;
    public const int Mesh = 5;
    public const int Sound = 6;
    public const int Texture = 7;
    public const int Game = 8;
    public const int PhysicsBody = 9;
    public const int RenderCamera = 12;
    public const int RenderLight = 13;
    public const int RenderObject = 14;
    public const int Viewport = 18;
    public const int Animation = 19;
}
