using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

//S.A.L. | Station Automation and Logistics
/* To-do: 
 *                                                    Done?
 * Reserve workbench----------------------------------DONE
 * Resolve pawn error on map load---------------------DONE
 * Check if workbench has power-----------------------DONE
 * Take bill off bill stack once done-----------------DONE
 * change colour of ouput direction in Draw()---------DONE
 * Make them be bad at art----------------------------DONE
 * Allow/disallow taking forbidden items--------------DONE
 * Make sound while crafting--------------------------DONE
 * Make sound when sucking item in--------------------DONE
 * Check if work table is deconstructed---------------DONE
 * Clear reservation if power off---------------------DONE
 * Customisable pawns via defs; skills----------------DONE
 * Sort out problem with nutrition/cooking------------DONE
 * Eject items in thingRecord if deconstructed--------DONE
 * Edit defs: Not forbiddable, flickable--------------DONE
 * Make smart hopper----------------------------------DONE
 * Check for small volume-----------------------------DONE
 * Redo corpse calculations---------------------------DONE
 * Add ingredients to unfinished items----------------DONE
 * Patch for Mending
 * Maintenance intervals------------------------------DONE
 * Move AssemblerDef to a ModExtension----------------DONE
 * Tiered crafters------------------------------------DONE
 *From xlilcasper (Ludeon Forums)
 * Let items get accepted from adjacent cells---------DONE
 * Check if colony has enough resources for bill
 *From Kadan Joelavich (Steam)
 * "This may not be possible, but would there 
 * be any way to have their global work speed 
 * factor in the material they are make from?---------DONE
 * Rework smart hopper
 * */
namespace ProjectSAL
{
    public partial class Building_Crafter : Building
    {
        #region Fields
        public Rot4 rotOutput = Rot4.South;
        public RecipeDef currentRecipe;
        public float workLeft;
        public List<_IngredientCount> ingredients = new List<_IngredientCount>();
        public List<Thing> thingRecord = new List<Thing>();
        public List<Thing> thingPlacementQueue = new List<Thing>();
        public bool allowForbidden = true;
        public Pawn buildingPawn;
        [Unsaved]
        public Sustainer sustainer;
        /// <summary>
        /// Cache only. <see cref="CheckIfShouldActivate"/>
        /// </summary>
        [Unsaved]
        bool cachedShouldActivate = true;
        #endregion

        #region Nutrition/Small volume calculations
        protected static bool ShouldUseNutritionMath(Thing t, _IngredientCount ingredient)
        {
            return (t.def.ingestible?.nutrition ?? 0f) > 0f && !(t is Corpse) && IngredientFilterHasNutrition(ingredient.filter);
        }

        protected static bool IngredientFilterHasNutrition(ThingFilter filter)
        {
            if (filter != null)
            {
                Func<string, bool> isNutrition = str => str == "Foods" || str == "PlantMatter";
                var field = typeof(ThingFilter).GetField("categories", BindingFlags.NonPublic | BindingFlags.Instance);
                var categories = (List<string>)(field.GetValue(filter) ?? new List<string>());
                foreach (string s in categories)
                {
                    if (DefDatabase<ThingCategoryDef>.GetNamed(s).Parents.Select(t => t.defName).Any(isNutrition) || isNutrition(s)) return true;
                }
            }
            return false;
        }
        protected static int CalculateIngredientIntFinalised(Thing item, _IngredientCount ingredient)
        {
            float basecount = ingredient.count;
            if (ShouldUseNutritionMath(item, ingredient))
            {
                basecount /= item.def.ingestible.nutrition;
            }
            if (item.def.smallVolume)
            {
                basecount /= 0.05f;
            }
            return Mathf.RoundToInt(basecount);
        }

        protected static float CalculateBaseCountFinalised(Thing item, _IngredientCount ingredient)
        {
            float basecount = item.stackCount;
            if (ShouldUseNutritionMath(item, ingredient))
            {
                basecount *= item.def.ingestible.nutrition;
            }
            if (item.def.smallVolume)
            {
                basecount *= 0.05f;
            }
            return basecount;
        }
        #endregion

        /// <summary>
        /// Makes a pawn.
        /// </summary>
        public virtual void DoPawn()
        {
            Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Slave, Faction);
            p.Name = new NameTriple(LabelCap, "SAL_Name".Translate(), GetUniqueLoadID());
            //Assign skills
            foreach (var s in p.skills.skills)
            {
        		int level = Extension.FindSkillAndGetLevel(s.def, Extension.defaultSkillLevel);
                s.levelInt = level;
                ProjectSAL_Utilities.Message("Successfully assigned level " + level + " for " + s.def.defName + " to buildingPawn.", 5);
            }
            var fieldInfo = typeof(Thing).GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance);
            //Assign Pawn's mapIndexOrState to building's mapIndexOrState
            fieldInfo.SetValue(p, fieldInfo.GetValue(this));
            //Assign Pawn's position without nasty errors
            p.SetPositionDirect(Position);
            //Clear pawn relations
            p.relations.ClearAllRelations();
            //Pawn work-related stuffs
            for (int i = 0; i < 24; i++)
            {
                p.timetable.SetAssignment(i, TimeAssignmentDefOf.Work);
            }

            buildingPawn = p;
        }

        public virtual void SetRecipe(Bill b)
        {
            ProjectSAL_Utilities.Message("Setting recipe: " + b, 3);
            currentRecipe = b.recipe;
            ingredients = new List<_IngredientCount>();
            (currentRecipe.ingredients ?? new List<IngredientCount>()).ForEach(ing => ingredients.Add(ing));//implicit cast
        }

        #region Products
        public virtual void TryMakeProducts()
        {
            if (currentRecipe == null)
            {
                Log.Warning(ToString() + " had workLeft > 0 when the currentRecipe is NULL. Resetting. (workLeft probably isn't synchronised with recipe. Use resetRecipe() to set currentRecipe to NULL and to synchronise workLeft.)");
                ResetRecipe();
                return;
            }
            foreach (Thing obj in GenRecipe.MakeRecipeProducts(currentRecipe, buildingPawn, thingRecord, CalculateDominantIngredient(currentRecipe, thingRecord)))
            {
                thingPlacementQueue.Add(obj);
                ProjectSAL_Utilities.Message("Thing added to queue. Thing was: " + obj + " x " + obj.stackCount, 2);
            }
            ProjectSAL_Utilities.Message("Current thingPlacementQueue length is: " + thingPlacementQueue.Count, 2);
            FindBillAndChangeRepeatCount(BillStack, currentRecipe);
            ResetRecipe();
        }

        public virtual void TryOutputItem()
        {
            ProjectSAL_Utilities.Message(string.Format("ThingPlacementQueue length: {0}, OutputSlotOccupied: {1}", thingPlacementQueue.Count, OutputSlotOccupied), 1);
            if (!OutputSlotOccupied && thingPlacementQueue.Count > 0)
            {
                GenPlace.TryPlaceThing(thingPlacementQueue.First(), OutputSlot, Map, ThingPlaceMode.Direct);
                thingPlacementQueue.RemoveAt(0);
            }
            else if (thingPlacementQueue.Count > 0)
            {
                foreach (var t in thingPlacementQueue)
                {
                    var thing = OutputSlot.GetThingList(Map).Find(th => th.CanStackWith(t));
                    thing?.TryAbsorbStack(t, true);
                    if (t.Destroyed || t.stackCount == 0)
                    {
                        thingPlacementQueue.Remove(t);
                        break;
                    }
                }
            }
        }
        #endregion
        
        public void PlayDropSound(Thing t)
        {
            if (t.def.soundDrop != null)
                t.def.soundDrop.PlayOneShot(SoundInfo.InMap(new TargetInfo(this)));
        }

        #region Resetting
        public virtual void ResetRecipe()
        {
            currentRecipe = null;
            ingredients.Clear();
            thingRecord.ForEach(t => t.Destroy());
            thingRecord.Clear();
            workLeft = 0;
            ReleaseAll();
        }

        public void DropAllThings()
        {
            if (currentRecipe == null) return;
            if (!currentRecipe.UsesUnfinishedThing)
            {
                foreach (var t in thingRecord)
                {
                    if (!t.Spawned) GenPlace.TryPlaceThing(t, Position, Map, ThingPlaceMode.Near);
                }
            }
            else
            {
                var stuff = (currentRecipe.unfinishedThingDef.MadeFromStuff) ? CalculateDominantIngredient(currentRecipe, thingRecord).def : null;
                var unfinished = (UnfinishedThing)ThingMaker.MakeThing(currentRecipe.unfinishedThingDef, stuff);
                unfinished.workLeft = workLeft;
                unfinished.ingredients = thingRecord;
                GenPlace.TryPlaceThing(unfinished, Position, Map, ThingPlaceMode.Near);
            }
            thingRecord.Clear();
        }

        public bool ResetIfWorkTableIsNull()
        {
            var isNull = WorkTable == null;
            if (isNull)
            {
                DropAllThings();
                ResetRecipe();
            }
            return isNull;
        }
        #endregion

        #region Reservation
        public void TryReserve(Thing thing = null)
        {
			if (thing == null) 
			{
                if (WorkTable == null)
                {
                    Log.Error("Tried to reserve workTable but workTable was null.");
                    return;
                }
                thing = WorkTable;
			}
			Map.physicalInteractionReservationManager.Reserve(buildingPawn, new LocalTargetInfo(thing));
            //Automatically checks if already reserved in core game code
			if (Map.reservationManager.CanReserve(buildingPawn, new LocalTargetInfo(thing))) Map.reservationManager.Reserve(buildingPawn, new LocalTargetInfo(thing));
			else ProjectSAL_Utilities.Message("Could not reserve thing " + thing, 4);
        }
        
        public void ReleaseAll()
        {
        	Map.physicalInteractionReservationManager.ReleaseAllClaimedBy(buildingPawn);
        	Map.reservationManager.ReleaseAllClaimedBy(buildingPawn);
        }
        #endregion
    }
}
