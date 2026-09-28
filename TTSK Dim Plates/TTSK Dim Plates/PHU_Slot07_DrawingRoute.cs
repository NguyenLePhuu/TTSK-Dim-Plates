namespace Tekla.Technology.Akit.UserScript
{
    internal enum Slot07DrawingRoute
    {
        Beam,
        LegacyColumn,
        HollowColumn,
        UnsupportedColumn
    }

    /// <summary>Single Slot 07 decision made before any beam context or writer.
    /// Orientation comes from authoritative MainPart REF in global coordinates;
    /// profile families come from the existing product profile classifier.</summary>
    internal static class PHU_Slot07_DrawingRoute
    {
        internal static Slot07DrawingRoute Resolve(bool vertical, bool legacyShape, bool hollow)
        {
            if (!vertical) return Slot07DrawingRoute.Beam;
            if (legacyShape) return Slot07DrawingRoute.LegacyColumn;
            if (hollow) return Slot07DrawingRoute.HollowColumn;
            return Slot07DrawingRoute.UnsupportedColumn;
        }
    }
}
