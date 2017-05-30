using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace ProjectSAL
{
    public partial class Building_Crafter
    {
        public ModExtension_Assembler Extension => def.GetModExtension<ModExtension_Assembler>();

        public IntVec3 OutputSlot => Position + GenAdj.CardinalDirections[0].RotatedBy(rotOutput);

        public List<Thing> NextItems
        {
            get
            {
                var things = new List<Thing>();
                foreach (var c in GenAdj.CellsAdjacent8Way(this))
                {
                    foreach (var t in c.GetThingList(Map))
                    {
                        if (t.def.category == ThingCategory.Item)
                            things.Add(t);
                    }
                }

                return things;
            }
        }

        public IntVec3 WorkTableCell => Position + GenAdj.CardinalDirections[Rotation.AsInt];

        public Building_WorkTable WorkTable => Map.thingGrid.ThingsListAt(WorkTableCell).OfType<Building_WorkTable>().Where(t => t.InteractionCell == Position).TryRandomElement(out Building_WorkTable result) ? result : null;

        public BillStack BillStack => WorkTable?.BillStack;

        protected bool OutputSlotOccupied => OutputSlot.GetFirstItem(Map) != null || OutputSlot.Impassable(Map);

        protected bool ShouldDoWork => currentRecipe != null && !ingredients.Any(ingredient => ingredient.count > 0) && ShouldDoWorkInCurrentTimeAssignment;

        protected bool ShouldStartBill => currentRecipe == null && BillStack != null && BillStack.AnyShouldDoNow;

        protected bool ShouldDoWorkInCurrentTimeAssignment => buildingPawn.timetable.times[GenLocalDate.HourOfDay(this)] != TimeAssignmentDefOf.Sleep;

        protected bool WorkDone => currentRecipe != null && ShouldDoWork && (int)workLeft == 0;

        protected SoundDef SoundOfCurrentRecipe => currentRecipe?.soundWorking;

        protected bool WorkTableisReservedByOther
        {
            get
            {
                if (WorkTable == null) return false;
                var target = new LocalTargetInfo(WorkTable);
                return Map.reservationManager.IsReserved(target, Faction) && !Map.physicalInteractionReservationManager.IsReservedBy(buildingPawn, target) && !Map.reservationManager.ReservedBy(target, buildingPawn);
            }
        }

        /// <summary>
        /// If worktable is reserved by someone else, or dependent on power and has no power, return false
        /// </summary>
        protected bool WorkTableIsDisabled => WorkTable != null && (WorkTableisReservedByOther || WorkTableIsPoweredOff);

        protected bool WorkTableIsPoweredOff => !(WorkTable.GetComp<CompPowerTrader>()?.PowerOn ?? true) && (!(WorkTable.GetComp<CompBreakdownable>()?.BrokenDown ?? false));

        /// <summary>
        /// If worktable has no bills that we should do now, return true
        /// </summary>
        protected bool WorkTableIsDormant => !(BillStack?.AnyShouldDoNow ?? false);
    }
}
