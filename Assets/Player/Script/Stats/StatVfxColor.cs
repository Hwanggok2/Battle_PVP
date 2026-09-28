using UnityEngine;

namespace BattlePvp.Stats
{
    public static class StatVfxColor
    {
        public static Color Resolve(Identity identity, StatContainer stats, Color str, Color agi, Color con, Color def)
        {
            Color primary = identity.PrimaryStat switch { StatKind.STR => str, StatKind.AGI => agi, StatKind.CON => con, _ => def };
            if (identity.Type == IdentityType.Monostat) return primary;
            float s = Mathf.Max(0, stats.STR.Invested), a = Mathf.Max(0, stats.AGI.Invested);
            float c = Mathf.Max(0, stats.CON.Invested), d = Mathf.Max(0, stats.DEF.Invested);
            Color result; float total;
            if (identity.Type == IdentityType.Strategist)
            {
                float first = s, second = -1; Color firstColor = str, secondColor = str;
                Consider(a, agi, ref first, ref firstColor, ref second, ref secondColor);
                Consider(c, con, ref first, ref firstColor, ref second, ref secondColor);
                Consider(d, def, ref first, ref firstColor, ref second, ref secondColor);
                total = first + second; result = firstColor * first + secondColor * second;
            }
            else { total = s + a + c + d; result = str * s + agi * a + con * c + def * d; }
            if (total <= .0001f) return primary;
            result /= total;
            float max = Mathf.Max(result.r, Mathf.Max(result.g, result.b));
            if (max > .0001f && max < 1) { result.r /= max; result.g /= max; result.b /= max; }
            result.a = 1; return result;
        }
        private static void Consider(float value, Color color, ref float first, ref Color firstColor, ref float second, ref Color secondColor)
        {
            if (value > first) { second = first; secondColor = firstColor; first = value; firstColor = color; }
            else if (value > second) { second = value; secondColor = color; }
        }
    }
}
