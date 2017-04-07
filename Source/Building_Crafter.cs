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
/*
 * To-do:                                         Done?
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
        public IntVec3 outputSlot
        {
            get
            {
                return Position + GenAdj.CardinalDirections[0].RotatedBy(rotOutput);
            }
        }

        public IntVec3 inputSlot
        {
            get
            {
                return Position + GenAdj.CardinalDirections[0].RotatedBy(rotInput);
            }
        }

        /// <summary>
        /// Gets next items in output slot for filtering.
        /// </summary>
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

        public IntVec3 workTableCell
        {
            get
            {
                return Position + GenAdj.CardinalDirections[Rotation.AsInt];
            }
        }

        public Building_WorkTable workTable
        {
            get
            {
                Building_WorkTable result;
                return Map.thingGrid.ThingsListAt(workTableCell).OfType<Building_WorkTable>().Where(t => t.InteractionCell == Position).TryRandomElement(out result) ? result : null;
            }
        }

        public BillStack billStack
        {
            get
            {
                return workTable == null ? null : workTable.BillStack;
            }
        }
        
        protected bool OutputSlotOccupied
        {
            get
            {
                return outputSlot.GetFirstItem(Map) != null || outputSlot.Impassable(Map);
            }
        }
        
        protected bool shouldDoWork
        {
            get
            {
                return currentRecipe != null && ingredients.Any() && !ingredients.Any(ingredient => ingredient.count > 0);
            }
        }
        
        protected bool shouldStartBill
        {
            get
            {
                return currentRecipe == null && billStack != null && billStack.AnyShouldDoNow;
            }
        }
        
        protected bool workDone
        {
            get
            {
                return currentRecipe != null && shouldDoWork && (int)workLeft == 0;
            }
        }
        
        protected SoundDef soundOfCurrentRecipe
        {
            get
            {
                return currentRecipe == null ? null : currentRecipe.soundWorking;
            }
        }
        
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
        protected bool workTableIsDisabled
        {
            get
            {
                return workTable != null && workTableisReservedByOther && workTable.GetComp<CompPowerTrader>() != null && !workTable.GetComp<CompPowerTrader>().PowerOn;
            }
        }
        
        //Constructors go here if needed
        
		public override void SpawnSetup(Map map)
		{
			base.SpawnSetup(map);
            if (buildingPawn == null)
            {
                doPawn();
                ProjectSAL_Utilities.Message("made new pawn.", 3);
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
                defaultLabel = "Adjust output direction",
                defaultDesc = "Click to rotate clockwise. Current output direction is: " + rotOutput.asCompassDirection(),
                activateSound = SoundDefOf.Click,
                action = () => rotOutput.Rotate(RotationDirection.Clockwise)
            };
            yield return new Command_Action
            {
                icon = ContentFinder<Texture2D>.Get("UI/Misc/Compass"),
                defaultLabel = "Adjust input direction",
                defaultDesc = "Click to rotate clockwise. Current input direction is: " + rotInput.asCompassDirection(),
                activateSound = SoundDefOf.Click,
                action = () => rotInput.Rotate(RotationDirection.Clockwise)
            };
            yield return new Command_Toggle
            {
                icon = ContentFinder<Texture2D>.Get("Things/Special/ForbiddenOverlay"),
                defaultLabel = "Toggle allow forbidden",
                defaultDesc = "Allow/Disallow the auto-assembler to take forbidden items.",
                isActive = () => allowForbidden,
                toggleAction = () => allowForbidden = !allowForbidden
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
            stringBuilder.AppendLine(string.Format("Current configuration: Input: {0}, Output: {1}", rotInput.asCompassDirection(), rotOutput.asCompassDirection()));
            stringBuilder.AppendLine("Work left: " + workLeft.ToStringWorkAmount());
            if (!GetComp<CompPowerTrader>().PowerOn)
            {
                stringBuilder.Append("Power is off.");
            }
            else
            {
                stringBuilder.AppendLine("Resources needed: ");
                foreach (_IngredientCount ingredient in ingredients)
                {
                    var str = ingredient.ToString();
                    stringBuilder.Append(str + " ");//(75 x Steel), (63 x Wood), etc
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
                thingRecord.ForEach(t => GenPlace.TryPlaceThing(t, Position, Map, ThingPlaceMode.Near));
            else
            {
                var stuff = (currentRecipe.unfinishedThingDef.MadeFromStuff) ? CalculateDominantIngredient(currentRecipe, thingRecord).def : null;
                var unfinished = (UnfinishedThing)ThingMaker.MakeThing(currentRecipe.unfinishedThingDef, stuff);
                unfinished.workLeft = workLeft;
                GenPlace.TryPlaceThing(unfinished, Position, Map, ThingPlaceMode.Near);
            }
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

        void acceptEachItem(Thing t)
        {
            if (t.TryGetComp<CompForbiddable>() != null && t.TryGetComp<CompForbiddable>().Forbidden && !allowForbidden)
                return;
            ingredients.ForEach(ingredient => acceptEachIngredient(ingredient, t));
        }

        void acceptEachIngredient(_IngredientCount ingredient, Thing t)
        {
        	if ((decimal)ingredient.count == 0)
                return;
            PlayDropSound(t);
            AcceptItemWithFilter(t, ingredient);
        }

        void AcceptItemWithFilter(Thing t, _IngredientCount ingredient)
        {
            if (ingredient.filter.Allows(t))
            {
            	float basecount = (t.def.ingestible != null && t.def.ingestible.nutrition > 0 && !(t is Corpse)) ? t.def.ingestible.nutrition * t.stackCount : t.stackCount; //Used for nutrition calculations.
                if (ingredient.count >= basecount)
                {
                    if (!thingRecord.Any(thing => t.def == thing.def))
                        thingRecord.Add(t);
                    ingredient.count -= basecount;
                    Map.thingGrid.Deregister(t);
                    Map.dynamicDrawManager.DeRegisterDrawable(t);
                }
                else
                {
                    if (!thingRecord.Any(thing => t.def == thing.def))
                        thingRecord.Add(makeDuplicateOf(t, (int)ingredient.Count));
                    basecount -= ingredient.Count;
                    t.stackCount = Mathf.RoundToInt((t.def.ingestible != null && t.def.ingestible.nutrition > 0) ? basecount / t.def.ingestible.nutrition : basecount);
                    ingredient.count = 0;
                }
            }
        }

        Thing makeDuplicateOf(Thing t, int count)
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
            p.Name = new NameTriple("Crafter", "a Project S.A.L. crafter", GetUniqueLoadID());
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
        
        public void TryReserve()
        {
			if (workTable == null) 
			{
				Log.Error("Tried to reserve but workTable was null.");
				return;
			}
			Map.physicalInteractionReservationManager.Reserve(buildingPawn, new LocalTargetInfo(workTable));
			if (Map.reservationManager.CanReserve(buildingPawn, new LocalTargetInfo(workTable))) Map.reservationManager.Reserve(buildingPawn, new LocalTargetInfo(workTable));
			else ProjectSAL_Utilities.Message("Could not reserve.", 4);
        }
        
        public void releaseAll()
        {
        	Map.physicalInteractionReservationManager.ReleaseAllClaimedBy(buildingPawn);
        	Map.reservationManager.ReleaseAllClaimedBy(buildingPawn);
        }

        public static Thing CalculateDominantIngredient(RecipeDef currentRecipe, List<Thing> thingRecord)
        {
            var stuffs = thingRecord.Where(t => t.def.IsStuff);
            if (thingRecord.NullOrEmpty())
            {
                Log.Warning("ThingRecord was null.");
                return null;
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
}
