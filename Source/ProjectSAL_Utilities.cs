using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using UnityEngine;

namespace ProjectSAL
{
    static class ProjectSAL_Utilities
    {
    	/// <summary>
    	/// This value is normally revision number + 1
    	/// </summary>
		public static int minImportance = 8;
        public static bool FFPresent
        {
            get
            {
                return ModsConfig.ActiveModsInLoadOrder.Any(d => d.Name == "SS Factory Framework");
            }
        }
        /// <summary>
        /// If built using the DEBUG constant, it will log a message if value of importance >= minImportance.
        /// </summary>
        public static void Message(string str, int importance)
        {
        	#if DEBUG
        	if (importance >= minImportance) Log.Message(str);
        	#endif
        }
        /// <summary>
        /// Returns current Rot4 as a compass direction.
        /// </summary>
        public static string AsCompassDirection(this Rot4 rot)
        {
            switch (rot.AsByte)
            {
                case 0:
                    return "SAL_North".Translate();
                case 1:
                    return "SAL_East".Translate();
                case 2:
                    return "SAL_South".Translate();
                case 3:
                    return "SAL_West".Translate();
                default:
                    return "SAL_InvalidDirection".Translate();
            }
        }
        public static float CalculateCraftingSpeedFactor(this StatDef workSpeedStat, Pawn pawn, ModExtension_Assembler extension)
        {
            if (workSpeedStat == null || pawn == null) return 1f;
        	float basenum = workSpeedStat.defaultBaseValue;
            List<SkillNeed> skillNeedFactors = workSpeedStat.skillNeedFactors ?? new List<SkillNeed>();
            for (int i = 0; i < skillNeedFactors.Count; i++) 
        	{
                var skillNeed = skillNeedFactors[i];
                var extraFactor = extension.skills.Find(s => s.skillDef == skillNeed.skill)?.workSpeedFactorExtra ?? 1;
                basenum *= (skillNeed.FactorFor(pawn) * extraFactor);
            }
            return basenum;
        }
    }
    /// <summary>
    /// Programmer trick to save IngredientCount.
    /// </summary>
    public class _IngredientCount : IExposable
    {
        public ThingFilter filter = new ThingFilter();
        public float count = 1f;
        
        public float Count
        {
        	get
        	{
        		return count;
        	}
        }

        /// <summary>
        /// IMPORTANT DO NOT REMOVE
        /// </summary>
        public _IngredientCount()
        {

        }

        public _IngredientCount(ThingFilter f, float c)
        {
            filter = f;
            count = c;
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref filter, "filter");
            Scribe_Values.Look(ref count, "count");
        }
		public override string ToString()
		{
			return string.Concat(new object[]
			{
				"(",
				count,
				"x ",
			    filter.ToString(),
				")"
			});
		}
        public static explicit operator IngredientCount(_IngredientCount ingredient)
        {
            var New = new IngredientCount
            {
                filter = ingredient.filter,
            };
            New.SetBaseCount(ingredient.count);
            return New;
        }
        public static implicit operator _IngredientCount(IngredientCount old)
        {
            return new _IngredientCount(old.filter, old.GetBaseCount());
        }
    }
}
