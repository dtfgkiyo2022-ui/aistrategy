using Rts.Contracts;

namespace Rts.Presentation
{
    /// <summary>38.31 labels shared by the IMGUI and UI Toolkit economy surfaces.</summary>
    public static class GlassworksUiText
    {
        public static string Stock(EconomyView economy)
        {
            if (economy == null || !economy.GlassworksEnabled) return "";
            return UiText.T("Sand " + economy.Sand + "  Glass " + economy.Glass + "  Lens " + economy.Lens,
                "砂 " + economy.Sand + "　ガラス " + economy.Glass + "　レンズ " + economy.Lens);
        }

        public static string MissingMaterial(ResourceKind material)
            => UiText.T("Missing material: " + material, "不足材料：" + Name(material));

        public static string Name(ResourceKind material)
        {
            switch (material)
            {
                case ResourceKind.Sand: return UiText.T("sand", "砂");
                case ResourceKind.Glass: return UiText.T("glass", "ガラス");
                case ResourceKind.Lens: return UiText.T("lens", "レンズ");
                default: return material.ToString();
            }
        }

        public static string BuildingName(BuildingKind kind)
            => kind == BuildingKind.GlassWorks ? UiText.T("Glassworks", "ガラス工房") : kind.ToString();
    }
}
