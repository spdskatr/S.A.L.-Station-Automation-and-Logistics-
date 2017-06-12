using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Verse;

namespace ProjectSAL
{
    public partial class Building_Crafter
    {
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
            }
            var fieldInfo = typeof(Thing).GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance);
            //Assign Pawn's mapIndexOrState to building's mapIndexOrState
            fieldInfo.SetValue(p, fieldInfo.GetValue(this));
            //Assign Pawn's position without nasty errors
            p.SetPositionDirect(Position);
            //Clear pawn relations
            p.relations.ClearAllRelations();
            //Set backstories
            SetBackstoryAndSkills(ref p);
            //Pawn work-related stuffs
            for (int i = 0; i < 24; i++)
            {
                p.timetable.SetAssignment(i, TimeAssignmentDefOf.Work);
            }

            buildingPawn = p;
            //p.DoSkillsAnalysis();
            //TestBackstoryAndSkills(p);
        }

        private static void SetBackstoryAndSkills(ref Pawn p)
        {
            if (BackstoryDatabase.TryGetWithIdentifier("ChildSpy95", out Backstory bs))
            {
                p.story.childhood = bs;
            }
            else
            {
                Log.Error("Tried to assign child backstory ChildSpy95, but not found");
            }
            if (BackstoryDatabase.TryGetWithIdentifier("ColonySettler43", out Backstory bstory))
            {
                p.story.adulthood = bstory;
            }
            else
            {
                Log.Error("Tried to assign child backstory ColonySettler43, but not found");
            }
            //Clear traits
            p.story.traits.allTraits = new List<Trait>();
            //Reset cache
            typeof(Pawn_StoryTracker).GetField("cachedDisabledWorkTypes", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(p.story, null);
        }
        private static void TestBackstoryAndSkills(Pawn p)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("Beginning story analysis.");
            stringBuilder.AppendFormat("BACKSTORY: Childhood: {0} Adulthood: {1}\n", p.story.childhood, p.story.adulthood);
            stringBuilder.AppendLine("TRAITS TEST");
            foreach (var t in p.story.traits.allTraits)
            {
                stringBuilder.Append(t);
            }
            stringBuilder.AppendLine("END TRAITS TEST");
            foreach (var w in p.story.DisabledWorkTypes)
            {
                stringBuilder.AppendLine("Disabled WorkType: " + w);
            }
            Log.Message(stringBuilder.ToString());
        }
    }
}