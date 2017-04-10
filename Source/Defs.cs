﻿using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace ProjectSAL
{
	public class AssemblerDef : ThingDef
	{
		public List<SkillLevel> skills = new List<SkillLevel>();
		public int FindSkillAndGetLevel(SkillDef skillDef)
		{
			for (int i = 0; i < skills.Count; i++) 
			{
				if (skills[i].skillDef == skillDef)
				{
					return skills[i].level;
				}
			}
			return 5;
		}
	}
	public class SkillLevel
	{
		public SkillDef skillDef;
		public int level = 5;
	}
}