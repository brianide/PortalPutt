using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;

namespace PortalPutt
{
	static class Extensions
	{
		public static T GetOrDefault<T>(this T[] thing, int a, T def = default)
		{
			if (a < 0 || a >= thing.Length)
				return def;
			return thing[a];
		}
	}

	static class Util
	{
		private static readonly int[,] Portals = PortalHelper.FoundPortals;

		public static IEnumerable<(Projectile pin, Projectile pout)> GetPortalPairs()
		{
			for (int i = 0; i < Portals.GetLength(0); i++)
				if (Main.projectile.GetOrDefault(Portals[i, 0]) is Projectile a && Main.projectile.GetOrDefault(Portals[i, 1]) is Projectile b)
				{
					yield return (a, b);
					yield return (b, a);
				}
		}
	}
}
