﻿using System;
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
 *                                                Done?
 * Reserve workbench------------------------------DONE
 * Resolve pawn error on map load-----------------DONE
 * Check if workbench has power-------------------DONE
 * Take bill off bill stack once done-------------DONE
 * change colour of ouput direction in Draw()-----DONE
 * Make them be bad at art------------------------DONE
 * Allow/disallow taking forbidden items----------DONE
 * Make sound while crafting----------------------DONE
 * Make sound when sucking item in----------------DONE
 * Check if work table is deconstructed-----------DONE
 * Clear reservation if power off-----------------DONE
 * Customisable pawns via defs; skills------------DONE
 * Sort out problem with nutrition/cooking--------DONE
 * Eject items in thingRecord if deconstructed----DONE
 * Edit defs: Not forbiddable, flickable----------DONE
 * Make smart hopper------------------------------DONE
 * */
namespace ProjectSAL
{
    public class Building_Crafter : Building
    {
        public Rot4 rotInput = Rot4.South;
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

		public IntVec3 outputSlot => Position + GenAdj.CardinalDirections[0].RotatedBy(rotOutput);

		public IntVec3 inputSlot => Position + GenAdj.CardinalDirections[0].RotatedBy(rotInput);

        public List<Thing> nextItems
        {
            get
            {
                var things = new List<Thing>();
                inputSlot.GetThingList(Map).ForEach(t =>
                {
                    if (t.def.category == ThingCategory.Item)
                        things.Add(t);
                });
                return things;
            }
        }

		public IntVec3 workTableCell => Position + GenAdj.CardinalDirections[Rotation.AsInt];

        public Building_WorkTable workTable => Map.thingGrid.ThingsListAt(workTableCell).OfType<Building_WorkTable>().Where(t => t.InteractionCell == Position).TryRandomElement(out Building_WorkTable result) ? result : null;

		public BillStack billStack => workTable == null ? null : workTable.BillStack;

		protected bool OutputSlotOccupied => outputSlot.GetFirstItem(Map) != null || outputSlot.Impassable(Map);
        
        protected bool shouldDoWork => currentRecipe != null && !ingredients.Any(ingredient => ingredient.count > 0);
        
		protected bool shouldStartBill => currentRecipe == null && billStack != null && billStack.AnyShouldDoNow;

		protected bool workDone => currentRecipe != null && shouldDoWork && (int)workLeft == 0;

		protected SoundDef soundOfCurrentRecipe => currentRecipe == null ? null : currentRecipe.soundWorking;
		
        protected bool workTableisReservedByOther
        {
        	get
        	{
        		if (workTable == null) return false;
        		var target = new LocalTargetInfo(workTable);
        		return Map.reservationManager.IsReserved(target, Faction) && !Map.physicalInteractionReservationManager.IsReservedBy(buildingPawn, target) && !Map.reservationManager.ReservedBy(target, buildingPawn);
        	}
        }
        
        /// <summary>
        /// If worktable is reserved by someone else, or dependent on power and has no power, return false
        /// </summary>
		protected bool workTableIsDisabled => workTable != null && this.workTableisReservedByOther && workTableIsPoweredOff;
		
		protected bool workTableIsPoweredOff => (workTable.GetComp<CompPowerTrader>() != null && !workTable.GetComp<CompPowerTrader>().PowerOn) || (workTable.GetComp<CompBreakdownable>() != null && workTable.GetComp<CompBreakdownable>().BrokenDown);

        //Constructors go here if needed
        
		public override void SpawnSetup(Map map)
		{
			base.SpawnSetup(map);
            if (buildingPawn == null)
            {
                doPawn();
                ProjectSAL_Utilities.Message("made new pawn.", 3);
            }
            else
            {
                var fieldInfo = typeof(Thing).GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance);
                var positionInfo = typeof(Thing).GetField("positionInt", BindingFlags.NonPublic | BindingFlags.Instance);
                //Assign Pawn's mapIndexOrState to building's mapIndexOrState
                fieldInfo.SetValue(buildingPawn, fieldInfo.GetValue(this));
                //Assign Pawn's position without nasty errors
                positionInfo.SetValue(buildingPawn, Position);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.LookDeep(ref buildingPawn, "pawn");
            Scribe_Defs.LookDef(ref currentRecipe, "currentRecipe");
            Scribe_Values.LookValue(ref workLeft, "workLeft");
            Scribe_Values.LookValue(ref rotInput, "rotInput");
            Scribe_Values.LookValue(ref rotOutput, "rotOutput");
            Scribe_Values.LookValue(ref allowForbidden, "allowForbidden", true);
            Scribe_Collections.LookList(ref ingredients, "ingredients", LookMode.Deep);
            Scribe_Collections.LookList(ref thingRecord, "thingRecord", LookMode.Deep);
            Scribe_Collections.LookList(ref thingPlacementQueue, "placementQueue", LookMode.Deep);
            if (buildingPawn == null)
            {
                doPawn();
                ProjectSAL_Utilities.Message("made new pawn.", 3);
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            yield return new Command_Action
            {
                icon = ContentFinder<Texture2D>.Get("UI/Misc/Compass"),
                defaultLabel = "AdjustDirection_Output".Translate(),
                defaultDesc = "AdjustDirection_Desc".Translate(rotOutput.asCompassDirection()),
                activateSound = SoundDefOf.Click,
                action = () => rotOutput.Rotate(RotationDirection.Clockwise)
            };
            yield return new Command_Action
            {
                icon = ContentFinder<Texture2D>.Get("UI/Misc/Compass"),
                defaultLabel = "AdjustDirection_Input".Translate(),
                defaultDesc = "AdjustDirection_Desc".Translate(rotInput.asCompassDirection()),
                activateSound = SoundDefOf.Click,
                action = () => rotInput.Rotate(RotationDirection.Clockwise)
            };
            yield return new Command_Toggle
            {
                icon = ContentFinder<Texture2D>.Get("Things/Special/ForbiddenOverlay"),
                defaultLabel = "SALToggleForbidden".Translate(),
                defaultDesc = "SALToggleForbidden_Desc".Translate(),
                isActive = () => allowForbidden,
                toggleAction = () => allowForbidden = !allowForbidden
            };
            yield return new Command_Action
            {
                icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                defaultLabel = "SALCancelBills".Translate(),
                defaultDesc = "SALCancelBills_Desc".Translate(),
                activateSound = SoundDefOf.Click,
                action = () =>
                {
                    DropAllThings();
                    resetRecipe();
                }
            };
            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEBUG: Set workLeft to 1",
                    action = () => workLeft = 1,
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEBUG: Drop everything",
                    action = DropAllThings,
                };
            }
        }

        public override void Tick()
        {
            base.Tick();
			if (!GetComp<CompPowerTrader>().PowerOn) 
			{
				if (Map.reservationManager.IsReserved(new LocalTargetInfo(workTable), Faction)) releaseAll();
				ProjectSAL_Utilities.Message("Reset reservations.", 4);
				return;
			}
            if (shouldDoWork && soundOfCurrentRecipe != null && !workTableIsDisabled)
                playSustainer();
            if (workTable != null && !Map.reservationManager.IsReserved(new LocalTargetInfo(workTable), Faction)) TryReserve();
            if (Find.TickManager.TicksGame % 10 == 0)
                acceptItems();//once every 10 ticks
            if (Find.TickManager.TicksGame % 60 == 0)
                TickSecond();//once every 60 ticks
        }

        public virtual void TickSecond()
        {
            TryOutputItem();

            if (ResetIfWorkTableIsNull())
                return;
            if (shouldStartBill)
                setRecipe(billStack.FirstShouldDoNow);
            if (shouldDoWork)
            {
                if (workLeft <= 0)
                {
                    ThingDef mainIngDef = CalculateDominantIngredient(currentRecipe, thingRecord).def;
                    ProjectSAL_Utilities.Message("mainIngDef: " + mainIngDef.ToString(), 3);
                    workLeft = currentRecipe.WorkAmountTotal(mainIngDef);
                }
                doWork();
            }
            if (workDone)
                tryMakeProducts();
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            GenDraw.DrawFieldEdges(new List<IntVec3> { inputSlot });
            GenDraw.DrawFieldEdges(new List<IntVec3> { outputSlot }, Color.green);
            Graphics.DrawMesh(MeshPool.plane10, workTableCell.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays), Quaternion.identity, GenDraw.InteractionCellMaterial, 0);
        }

        public override string GetInspectString()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(base.GetInspectString());
            stringBuilder.AppendLine("SALInspect_CurrentConfig".Translate(rotInput.asCompassDirection(), rotOutput.asCompassDirection()));
            stringBuilder.AppendLine("SALInspect_WorkLeft".Translate(workLeft.ToStringWorkAmount()));
            stringBuilder.AppendLine("SALInspect_PlacementQueue".Translate(thingPlacementQueue.Count));
            if (!GetComp<CompPowerTrader>().PowerOn)
            {
                stringBuilder.Append("SALInspect_PowerOff".Translate());
            }
            else
            {
                stringBuilder.Append("SALInspect_ResourcesNeeded".Translate());
                foreach (_IngredientCount ingredient in ingredients)
                {
                    var str = ingredient.ToString();
                    stringBuilder.Append(str + " ");//(75 x Steel) (63 x Wood) etc
                }
            }
            return stringBuilder.ToString();
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
        	releaseAll();
            DropAllThings();
            base.Destroy(mode);
        }

        public void DropAllThings()
        {
            if (currentRecipe == null) return;
            if (!currentRecipe.UsesUnfinishedThing)
            {
                foreach (var t in thingRecord)
                {
                    if (t.Spawned)
                    {
                        Map.dynamicDrawManager.RegisterDrawable(t);
                        var listoflists = typeof(ListerThings).GetField("listsByGroup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Map.listerThings) as List<Thing>[];
                        var list = listoflists[(int)ThingRequestGroup.HasGUIOverlay];
                        list.Add(t);
                        continue;
                    }
                    else GenPlace.TryPlaceThing(t, Position, Map, ThingPlaceMode.Near);
                }
            }
            else
            {
                var stuff = (currentRecipe.unfinishedThingDef.MadeFromStuff) ? CalculateDominantIngredient(currentRecipe, thingRecord).def : null;
                var unfinished = (UnfinishedThing)ThingMaker.MakeThing(currentRecipe.unfinishedThingDef, stuff);
                unfinished.workLeft = workLeft;
                GenPlace.TryPlaceThing(unfinished, Position, Map, ThingPlaceMode.Near);
            }
            thingRecord.Clear();
        }

        /// <summary>
        /// Accepts a new load of items into ingredient list
        /// </summary>
        public void acceptItems()
        {
            ProjectSAL_Utilities.Message("Count of nextItems: " + nextItems.Count, 1);
            ProjectSAL_Utilities.Message("Count of ingredients: " + ingredients.Count, 1);
            nextItems.ForEach(acceptEachItem);
        }

        protected void acceptEachItem(Thing t)
        {
            if (t.TryGetComp<CompForbiddable>() != null 
                && (!t.TryGetComp<CompForbiddable>()?.Forbidden ?? false
                || allowForbidden)
                && Map.reservationManager.IsReserved(new LocalTargetInfo(t), Faction.OfPlayer))
                return;
            ingredients.ForEach(ingredient => acceptEachIngredient(ingredient, t));
        }

        protected void acceptEachIngredient(_IngredientCount ingredient, Thing t)
        {
        	if ((decimal)ingredient.count == 0)
                return;
            PlayDropSound(t);
            AcceptItemWithFilter(t, ingredient);
        }

        protected void AcceptItemWithFilter(Thing t, _IngredientCount ingredient)
        {
            if (ingredient.filter.Allows(t))
            {
            	float basecount = shouldUseNutritionMath(t, ingredient) ? t.def.ingestible.nutrition * t.stackCount : t.stackCount; //Used for nutrition calculations.
                if (ingredient.count >= basecount)
                {
                    Thing dup;
                    if (t is Corpse)
                    {
                        var corpse = t as Corpse;
                        corpse.Strip();
                        Map.dynamicDrawManager.DeRegisterDrawable(t);
                        var listoflists = typeof(ListerThings).GetField("listsByGroup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Map.listerThings) as List<Thing>[];
                        var list = listoflists[(int)ThingRequestGroup.HasGUIOverlay];
                        list.Remove(t);
                        t.Position = Position;
                        dup = t;
                    }
                    else
                    {
                        dup = t.SplitOff(t.stackCount);
                    }
                    if (!thingRecord.Any(thing => t.def == thing.def))
                        thingRecord.Add(dup);
                    else thingRecord.Find(thing => t.def == thing.def);
                    ingredient.count -= basecount;
                    
                    /*Map.thingGrid.Deregister(t);
                    Map.dynamicDrawManager.DeRegisterDrawable(t);
                    var listoflists = typeof(ListerThings).GetField("listsByGroup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Map.listerThings) as List<Thing>[];
                    var list = listoflists[(int)ThingRequestGroup.HasGUIOverlay];
                    list.Remove(t);
                    typeof(Thing).GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(t, (sbyte)-1);*/
                }
                else
                {
                    Thing dup = t.SplitOff(Mathf.RoundToInt(shouldUseNutritionMath(t, ingredient) ? ingredient.Count / t.def.ingestible.nutrition : ingredient.Count));
                    if (!thingRecord.Any(thing => t.def == thing.def))
                        thingRecord.Add(dup);
                    ingredient.count = 0;
                }
            }
        }

        protected static bool shouldUseNutritionMath(Thing t, _IngredientCount ingredient)
        {
            return t.def.ingestible != null && t.def.ingestible.nutrition > 0 && !(t is Corpse) && IngredientFilterHasNutrition(ingredient.filter);
        }

        protected static bool IngredientFilterHasNutrition(ThingFilter filter)
        {
            if (filter != null)
            {
                Func<string, bool> isNutrition = str => str == "Foods" || str == "PlantMatter";
                var field = typeof(ThingFilter).GetField("categories", BindingFlags.NonPublic | BindingFlags.Instance);
                var categories = (List<string>)field.GetValue(filter) ?? new List<string>();
                foreach (string s in categories)
                {
                    if (DefDatabase<ThingCategoryDef>.GetNamed(s).Parents.Select(t => t.defName).Any(isNutrition) || isNutrition(s)) return true;
                }
            }
            return false;
        }

        protected static Thing makeDuplicateOf(Thing t, int count)
        {
            var duplicate = ThingMaker.MakeThing(t.def, t.Stuff);
            duplicate.stackCount = count;
            return duplicate;
        }

        public virtual void doWork(int interval = 60)
        {
			if (workTableIsDisabled) 
			{
				releaseAll();
				return;
			} 
            if (workLeft > 0)
            {
                workLeft -= interval * currentRecipe.workSpeedStat.calculateCraftingSpeedFactor(buildingPawn);
                if (workLeft <= 0f)
                {
                    workLeft = 0f;
                }
            }
        }
        public virtual void tryMakeProducts()
        {
            if (currentRecipe == null)
            {
                Log.Warning(ToString() + " had workLeft > 0 when the currentRecipe is NULL. Resetting. (workLeft probably isn't synchronised with recipe. Use resetRecipe() to set currentRecipe to NULL and to synchronise workLeft.)");
                resetRecipe();
                return;
            }
            foreach (Thing obj in GenRecipe.MakeRecipeProducts(currentRecipe, buildingPawn, thingRecord, CalculateDominantIngredient(currentRecipe, thingRecord)))
            {
                thingPlacementQueue.Add(obj);
                ProjectSAL_Utilities.Message("Thing added to queue. Thing was: " + obj + " x " + obj.stackCount, 2);
            }
            ProjectSAL_Utilities.Message("Current thingPlacementQueue length is: " + thingPlacementQueue.Count, 2);
            FindBillAndChangeRepeatCount(billStack, currentRecipe);
            resetRecipe();
        }

        /// <summary>
        /// Makes a pawn.
        /// </summary>
        public virtual void doPawn()
        {
            Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Slave, Faction);
            p.Name = new NameTriple(LabelCap, "SAL_Name".Translate(), GetUniqueLoadID());
            foreach (var s in p.skills.skills)
            {
            	int level = 5;
            	var defAssembler = def as AssemblerDef;
        		if (defAssembler != null)
        		{
        		    level = defAssembler.FindSkillAndGetLevel(s.def);
        		}
                s.levelInt = level;
                ProjectSAL_Utilities.Message("Successfully assigned level " + level + " for " + s.def.defName + " to buildingPawn.", 5);
            }
            var fieldInfo = typeof(Thing).GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance);
            var positionInfo = typeof(Thing).GetField("positionInt", BindingFlags.NonPublic | BindingFlags.Instance);
            //Assign Pawn's mapIndexOrState to building's mapIndexOrState
            fieldInfo.SetValue(p, fieldInfo.GetValue(this));
            //Assign Pawn's position without nasty errors
            positionInfo.SetValue(p, Position);
            buildingPawn = p;
        }

        public virtual void setRecipe(Bill b)
        {
            ProjectSAL_Utilities.Message("Setting recipe: " + b, 3);
            currentRecipe = b.recipe;
            ingredients = new List<_IngredientCount>();
            (currentRecipe.ingredients ?? new List<IngredientCount>()).ForEach(ing => ingredients.Add(ing.toSaveable()));
        }

        public virtual void resetRecipe()
        {
            currentRecipe = null;
            ingredients.Clear();
            thingRecord.ForEach(t => t.Destroy());
            thingRecord.Clear();
            workLeft = 0;
            releaseAll();
        }
        public virtual void TryOutputItem()
        {
            ProjectSAL_Utilities.Message(string.Format("ThingPlacementQueue length: {0}, OutputSlotOccupied: {1}", thingPlacementQueue.Count, OutputSlotOccupied), 1);
            if (!OutputSlotOccupied && thingPlacementQueue.Count > 0)
            {
                GenPlace.TryPlaceThing(thingPlacementQueue.First(), outputSlot, Map, ThingPlaceMode.Direct);
                thingPlacementQueue.RemoveAt(0);
            }
            else if (thingPlacementQueue.Count > 0)
            {
                foreach (var t in thingPlacementQueue)
                {
                    var thing = outputSlot.GetThingList(Map).Find(th => th.CanStackWith(t));
                    thing?.TryAbsorbStack(t, true);
                    if (t.Destroyed || t.stackCount == 0)
                    {
                        thingPlacementQueue.Remove(t);
                        break;
                    }
                }
            }
        }
        

        public void playSustainer()
        {
            if (sustainer == null || sustainer.Ended)
            {
                var soundInfo = SoundInfo.InMap(new TargetInfo(this), MaintenanceType.PerTick);
                sustainer = soundOfCurrentRecipe.TrySpawnSustainer(soundInfo);
            }
            else
            {
                sustainer.Maintain();
            }
        }

        public void PlayDropSound(Thing t)
        {
            if (t.def.soundDrop != null)
                t.def.soundDrop.PlayOneShot(SoundInfo.InMap(new TargetInfo(this)));
        }

        public bool ResetIfWorkTableIsNull()
        {
            var isNull = workTable == null;
            if (isNull)
            {
                DropAllThings();
                resetRecipe();
            }
            return isNull;
        }

        public void TryReserve(Thing thing = null)
        {
			if (thing == null) 
			{
                if (workTable == null)
                {
                    Log.Error("Tried to reserve workTable but workTable was null.");
                    return;
                }
                thing = workTable;
			}
			Map.physicalInteractionReservationManager.Reserve(buildingPawn, new LocalTargetInfo(thing));
			if (Map.reservationManager.CanReserve(buildingPawn, new LocalTargetInfo(thing))) Map.reservationManager.Reserve(buildingPawn, new LocalTargetInfo(thing));
			else ProjectSAL_Utilities.Message("Could not reserve thing " + thing, 4);
        }
        
        public void releaseAll()
        {
        	Map.physicalInteractionReservationManager.ReleaseAllClaimedBy(buildingPawn);
        	Map.reservationManager.ReleaseAllClaimedBy(buildingPawn);
        }

        public static Thing CalculateDominantIngredient(RecipeDef currentRecipe, List<Thing> thingRecord)
        {
            var stuffs = thingRecord.Where(t => t.def.IsStuff);
            if (thingRecord == null)
            {
                Log.Warning("ThingRecord was null.");
                return null;
            }
            if (thingRecord.Count == 0)
            {
                return ThingMaker.MakeThing(ThingDefOf.Steel);
            }
            if (currentRecipe.productHasIngredientStuff)
            {
                return stuffs.OrderByDescending(t => t.stackCount).First();
            }
            if (currentRecipe.products.Any(x => x.thingDef.MadeFromStuff) || stuffs.Any())
            {
                return stuffs.RandomElementByWeight(x => x.stackCount);
            }
            return ThingMaker.MakeThing(ThingDefOf.Steel);
        }

        public static void FindBillAndChangeRepeatCount(BillStack billStack, RecipeDef currentRecipe)
        {
            if (billStack != null && billStack.Bills != null)
            {
                var billrepeater = billStack.Bills.OfType<Bill_Production>().ToList().Find(b => b.ShouldDoNow() && b.recipe == currentRecipe);
                if (billrepeater != null && billrepeater.repeatMode == BillRepeatMode.RepeatCount)
                    billrepeater.repeatCount -= 1;
            }
        }

    }
    public class Building_SmartHopper : Building_Storage
    {
        public int limit = 75;

        [Unsaved]
        public IEnumerable<IntVec3> cachedDetectorCells;

        protected virtual bool ShouldRespectStackLimit => true;

        public Thing StoredThing => Position.GetFirstItem(Map);

        public IEnumerable<IntVec3> CellsToSelect
        {
            get
            {
                if (Find.TickManager.TicksGame % 50 != 0 && cachedDetectorCells != null)
                {
                    ProjectSAL_Utilities.Message("Returning cache.", 7);
                    return cachedDetectorCells;
                }

                var resultCache = from IntVec3 c in (GenRadial.RadialCellsAround(Position, def.specialDisplayRadius, false) ?? new List<IntVec3>()) where c.GetZone(Map) != null && c.GetZone(Map) is Zone_Stockpile select c;
                cachedDetectorCells = resultCache;
                return resultCache;
            }
        }

        public IEnumerable<Thing> ThingsToSelect
        {
            get
            {
                foreach (var c in CellsToSelect)
                {
                    foreach (var t in Map.thingGrid.ThingsListAt(c))
                    {
                        yield return t;
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.LookValue(ref limit, "limit", 75);
        }

        public override string GetInspectString() => base.GetInspectString() + "SmartHopper_Limit".Translate(limit);

        public override void Tick()
        {
            base.Tick();
            if (GetComp<CompPowerTrader>().PowerOn && Find.TickManager.TicksGame % 10 == 0)
            {
                foreach (var element in ThingsToSelect)
                {
                    if (element.def.category == ThingCategory.Item && settings.AllowedToAccept(element))
                    {
                        if (StoredThing != null)
                        {
                            if (StoredThing.CanStackWith(element))
                            {
                                //Verse.ThingUtility.TryAsborbStackNumToTake <- That's not a typo, that's an actual method. Seriously. Good thing it's fixed in A17, eh? (LudeonLeaks, 2017)
                                var num = Mathf.Min(element.stackCount, Mathf.Min(limit, StoredThing.def.stackLimit) - StoredThing.stackCount);
                                if (num > 0)
                                {
                                    var t = element.SplitOff(num);
                                    StoredThing.TryAbsorbStack(t, true);
                                }
                            }
                        }
                        else
                        {
                            var num = Mathf.Min(element.stackCount, limit);
                            if (num == element.stackCount)
                            {
                                element.Position = Position;
                            }
                            else if (num > 0)
                            {
                                var t = element.SplitOff(num);
                                GenPlace.TryPlaceThing(t, Position, Map, ThingPlaceMode.Direct);
                            }
                        }
                        break;
                    }
                }
            }
        }
        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            GenDraw.DrawFieldEdges(CellsToSelect.ToList(), Color.green);
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
                yield return g;
            yield return new Command_Action
            {
                icon = ContentFinder<Texture2D>.Get("UI/Commands/SetTargetFuelLevel"),
                defaultLabel = "SmartHopper_SetTargetAmount".Translate(),
                action = () => Find.WindowStack.Add(new Dialog_SmartHopperSetTargetAmount(this)),
            };
        }
    }
}
