using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace ProjectSAL
{
    static class ProjectSAL_Utilities
    {
    	/// <summary>
    	/// This value is normally revision number + 1
    	/// </summary>
		public static int minImportance = 6;
        public static bool FFPresent
        {
            get
            {
                return ModsConfig.ActiveModsInLoadOrder.Any(d => d.Name == "SS Factory Framework");
            }
        }
        public static _IngredientCount toSaveable(this IngredientCount old)
        {
            return new _IngredientCount(old.filter, old.GetBaseCount());
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
        public static string asCompassDirection(this Rot4 rot)
        {
            switch (rot.AsByte)
            {
                case 0:
                    return "north";
                case 1:
                    return "east";
                case 2:
                    return "south";
                case 3:
                    return "west";
                default:
                    return "invalid";
            }
        }
        public static float calculateCraftingSpeedFactor(this StatDef workSpeedStat, Pawn pawn)
        {
            if (workSpeedStat == null || pawn == null) return 1f;
        	float basenum = workSpeedStat.defaultBaseValue;
            List<SkillNeed> skillNeedFactors = workSpeedStat.skillNeedFactors ?? new List<SkillNeed>();
            for (int i = 0; i < skillNeedFactors.Count; i++) 
        	{
        		basenum *= skillNeedFactors[i].FactorFor(pawn);
            }
            return basenum;
        }
        public static T makeDuplicateObject<T>(this T obj)
        {
            var newObj = Activator.CreateInstance(obj.GetType());
            foreach (var field in obj.GetType().GetFields())
            {
                newObj.GetType().GetFields().ToList().Find(f => f.Name == field.Name).SetValue(newObj, field.GetValue(obj));
            }
            return (T)newObj;
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
            Scribe_Deep.LookDeep(ref filter, "filter");
            Scribe_Values.LookValue(ref count, "count");
        }
        public IngredientCount toOriginal()
        {
            var New = new IngredientCount
            {
                filter = filter
            };
            New.SetBaseCount(count);
            return New;
        }
		public override string ToString()
		{
			return string.Concat(new object[]
			{
				"(",
				this.count,
				"x ",
				this.filter.ToString(),
				")"
			});
		}
    }
}
