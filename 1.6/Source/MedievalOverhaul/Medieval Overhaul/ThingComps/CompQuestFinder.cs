﻿using RimWorld;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MedievalOverhaul
{
    public class CompQuestFinder : CompScanner
    {
        private CompRefuelable compRefuelable;
        private QuestScriptDef currentQuest;
        private CompAffectedByFacilities facilities;
        private ThingDef targetMineable;
        private bool requiredBuilding = false;

        protected IEnumerable<QuestScriptDef> AvailableForFind
        {
            get
            {
                foreach (QuestScriptDef quest in QuestFinderUtility.PossibleQuests)
                {
                    if (this.CanFind(quest))
                    {
                        yield return quest;
                    }
                }
            }
        }


        protected virtual bool CanFind(QuestScriptDef quest)
        {
            QuestInformation ext = quest.FinderInfo();
            if (ext.requiredLinkable != null)
            {
                requiredBuilding = false;
                for (int i = 0; i < this.facilities.LinkedFacilitiesListForReading.Count; i++)
                {
                    if (this.facilities.LinkedFacilitiesListForReading[i] is Building building && building.def == ext.requiredLinkable)
                    {
                        var refuelComp = building.TryGetComp<CompRefuelableCustom>();
                        if (refuelComp == null || refuelComp.HasFuel)
                        {
                            requiredBuilding = true;
                        }
                        break;
                    }
                }
            }
            else
                requiredBuilding = true;
            
            return ext.LinkablesNeeded <= this.facilities.LinkedFacilitiesListForReading.Count && requiredBuilding && (!ext.onlyOnce || !GameComponent_QuestFinder.Instance.Completed(quest));
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            this.compRefuelable = this.parent.TryGetComp<CompRefuelable>();
            this.facilities = this.parent.TryGetComp<CompAffectedByFacilities>();
            this.currentQuest ??= this.AvailableForFind.RandomElement<QuestScriptDef>();
            GameComponent_QuestFinder.Instance.RegisterFinder(this);
        }
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            this.SetDefaultTargetMineral();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map);
            GameComponent_QuestFinder.Instance.DeregisterFinder(this);
        }

        public new void Used(Pawn worker)
		{
			if (!this.CanUseNow)
			{
				Log.Error("Used while CanUseNow is false.");
			}
			this.lastScanTick = (float)Find.TickManager.TicksGame;
			this.lastUserSpeed = 1f;
			if (this.Props.scanSpeedStat != null)
			{
				this.lastUserSpeed = worker.GetStatValue(this.Props.scanSpeedStat, true, -1);
			}
			this.daysWorkingSinceLastFinding += this.lastUserSpeed / 60000f;
			if (this.TickDoesFind(this.lastUserSpeed))
			{
				this.DoFind(worker);
				this.daysWorkingSinceLastFinding = 0f;
			}
		}

        public override void DoFind(Pawn worker)
        {
            if (this.currentQuest == QuestScriptDefOf.LongRangeMineralScannerLump)
            {
                Map map = parent.Map;
                if (!CellFinderLoose.TryFindRandomNotEdgeCellWith(10, (IntVec3 x) => CanScatterAt(x, map), map, out var result))
                {
                    Log.Error("Could not find a center cell for deep scanning lump generation!");
                }
                ThingDef thingDef = this.targetMineable;
                int numCells = Mathf.CeilToInt(thingDef.deepLumpSizeRange.RandomInRange);
                foreach (IntVec3 item in GridShapeMaker.IrregularLump(result, map, numCells))
                {
                    if (CanScatterAt(item, map) && !item.InNoBuildEdgeArea(map))
                    {
                        map.deepResourceGrid.SetAt(item, thingDef, thingDef.deepCountPerCell);
                    }
                }
                string key = ("LetterDeepScannerFoundLump".CanTranslate() ? "LetterDeepScannerFoundLump" : ((!"DeepScannerFoundLump".CanTranslate()) ? "LetterDeepScannerFoundLump" : "DeepScannerFoundLump"));
                Find.LetterStack.ReceiveLetter("LetterLabelDeepScannerFoundLump".Translate() + ": " + thingDef.LabelCap, key.Translate(thingDef.label, worker.Named("FINDER")), LetterDefOf.PositiveEvent, new LookTargets(result, map));
                // Slate slate = new Slate();
                // slate.Set<Map>("map", this.parent.Map, false);
                // slate.Set<ThingDef>("targetMineable", this.targetMineable, false);
                // slate.Set<ThingDef>("targetMineableThing", this.targetMineable.building.mineableThing, false);
                // slate.Set<Pawn>("worker", worker, false);
                // if (!QuestScriptDefOf.LongRangeMineralScannerLump.CanUseNow(slate))
                // {
                //     return;
                // }
                // Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(QuestScriptDefOf.LongRangeMineralScannerLump, slate);
                // Find.LetterStack.ReceiveLetter(quest.name, quest.description, LetterDefOf.PositiveEvent, null, null, quest, null, null, 0, true);
            }
            else
            {
                Quest andMakeAvailable = QuestUtility.GenerateQuestAndMakeAvailable(this.currentQuest, StorytellerUtility.DefaultSiteThreatPointsNow());
                GameComponent_QuestFinder.Instance.Notify_QuestIssued(andMakeAvailable);
                QuestUtility.SendLetterQuestAvailable(andMakeAvailable);
            }
        }

        public override bool TickDoesFind(float scanSpeed) => (double)this.daysWorkingSinceLastFinding >= (double)this.currentQuest.FinderInfo().WorkTillTrigger.TicksToDays();

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            IEnumerable<Gizmo> gizmosExtra = base.CompGetGizmosExtra();
            if (this.parent.Faction == Faction.OfPlayer)
            {
                Command_Action element = new Command_Action();
                element.defaultLabel = this.currentQuest.LabelCap();
                element.defaultDesc = (string)"EEG.SelectQuest".Translate((NamedArgument)this.currentQuest.LabelCap());
                element.icon = (Texture)TexButton.Search;
                element.action = (Action)(() =>
                {
                    List<FloatMenuOption> menuOptions = new List<FloatMenuOption>();

                    foreach (QuestScriptDef quest in this.AvailableForFind)
                    {
                        menuOptions.Add(new FloatMenuOption(quest.LabelCap(), (Action)(() => this.currentQuest = quest)));
                    }

                    Find.WindowStack.Add((Window)new FloatMenu(menuOptions));
                });


                yield return element;
            }

            if (this.parent.Faction == Faction.OfPlayer && this.currentQuest == QuestScriptDefOf.LongRangeMineralScannerLump)
            {
                ThingDef mineableThing = this.targetMineable.building.mineableThing;
                Command_Action command_Action = new Command_Action();
                command_Action.defaultLabel = "CommandSelectMineralToScanFor".Translate() + ": " + mineableThing.LabelCap;
                command_Action.defaultDesc = "CommandSelectMineralToScanForDesc".Translate();
                command_Action.icon = mineableThing.uiIcon;
                command_Action.iconAngle = mineableThing.uiIconAngle;
                command_Action.iconOffset = mineableThing.uiIconOffset;
                command_Action.action = delegate ()

                {
                    List<ThingDef> mineables = ((GenStep_PreciousLump)GenStepDefOf.PreciousLump.genStep).mineables;
                    List<FloatMenuOption> list = [];
                    foreach (ThingDef localD2 in mineables)
                    {
                        ThingDef localD = localD2;
                        FloatMenuOption item = new (localD.building.mineableThing.LabelCap, delegate ()
                        {
                            foreach (object obj in Find.Selector.SelectedObjects)
                            {
                                if (obj is Thing thing)
                                {
                                    CompQuestFinder compLongRangeMineralScanner = thing.TryGetComp<CompQuestFinder>();
                                    if (compLongRangeMineralScanner != null)
                                    {
                                        compLongRangeMineralScanner.targetMineable = localD;
                                    }
                                }
                            }
                        }, MenuOptionPriority.Default, null, null, 29f, (Rect rect) => Widgets.InfoCardButton(rect.x + 5f, rect.y + (rect.height - 24f) / 2f, localD.building.mineableThing), null, true, 0);
                        list.Add(item);
                    }
                    Find.WindowStack.Add(new FloatMenu(list));
                };
                yield return command_Action;
            }
            yield break;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look<QuestScriptDef>(ref this.currentQuest, "currentQuest");
            Scribe_Defs.Look<ThingDef>(ref this.targetMineable, "targetMineable");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && this.targetMineable == null)
            {
                this.SetDefaultTargetMineral();
            }
        }
        private void SetDefaultTargetMineral()
        {
            this.targetMineable = ThingDefOf.MineableGold;
        }
        public override string CompInspectStringExtra() => string.Format("{0}: {1}\n", (object)"EEG.SearchingFor".Translate(), (object)this.currentQuest.LabelCap()) + ((double)this.lastScanTick > (double)(Find.TickManager.TicksGame - 30) ? string.Format("{0}: {1}\n", (object)"UserScanAbility".Translate(), (object)this.lastUserSpeed.ToStringPercent()) : "") + string.Format("{0}: ", (object)"EEG.ScanningProgress".Translate()) + (this.daysWorkingSinceLastFinding / this.currentQuest.FinderInfo().WorkTillTrigger.TicksToDays()).ToStringPercent();

        public void Notify_QuestCompleted()
        {
            if (this.CanFind(this.currentQuest))
                return;
            this.currentQuest = this.AvailableForFind.RandomElement<QuestScriptDef>();
        }

        public new bool CanUseNow
        {
            get
            {
                return this.parent.Spawned &&
                  (this.powerComp == null || this.powerComp.PowerOn) &&
                  (this.forbiddable == null || !this.forbiddable.Forbidden) &&
                  this.parent.Faction.IsPlayerSafe() &&
                  (this.compRefuelable == null || this.compRefuelable.HasFuel);
            }
        }

        private bool CanScatterAt(IntVec3 pos, Map map)
        {
            int index = CellIndicesUtility.CellToIndex(pos, map.Size.x);
            TerrainDef terrainDef = map.terrainGrid.BaseTerrainAt(pos);
            if ((terrainDef != null && terrainDef.IsWater && terrainDef.passability == Traversability.Impassable) || !pos.GetAffordances(map).Contains(ThingDefOf.DeepDrill.terrainAffordanceNeeded))
            {
                return false;
            }
            return !map.deepResourceGrid.GetCellBool(index);
        }
    }
}
