﻿using ARKBreedingStats.Library;
using ARKBreedingStats.species;
using ARKBreedingStats.uiControls;
using ARKBreedingStats.values;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Threading;
using ARKBreedingStats.utils;
using System.IO;
using System.Text.RegularExpressions;
using ARKBreedingStats.library;

namespace ARKBreedingStats
{
    public partial class Form1
    {
        /// <summary>
        /// Creatures filtered according to the library-filter.
        /// Used so the live filter doesn't need to do the base filtering every time.
        /// </summary>
        private Creature[] _creaturesPreFiltered;

        /// <summary>
        /// Add a new creature to the library based from the data of the extractor or tester
        /// </summary>
        /// <param name="fromExtractor">if true, take data from extractor-infoInput, else from tester</param>
        /// <param name="motherArkId">only pass if from import. Used for creating placeholder parents</param>
        /// <param name="fatherArkId">only pass if from import. Used for creating placeholder parents</param>
        /// <param name="goToLibraryTab">go to library tab after the creature is added</param>
        private Creature AddCreatureToCollection(bool fromExtractor = true, long motherArkId = 0, long fatherArkId = 0, bool goToLibraryTab = true)
        {
            CreatureInfoInput input;
            bool bred;
            double te, imprinting;
            Species species = speciesSelector1.SelectedSpecies;
            if (fromExtractor)
            {
                input = creatureInfoInputExtractor;
                bred = rbBredExtractor.Checked;
                te = _extractor.UniqueTE();
                imprinting = _extractor.ImprintingBonus;
            }
            else
            {
                input = creatureInfoInputTester;
                bred = rbBredTester.Checked;
                te = (double)NumericUpDownTestingTE.Value / 100;
                imprinting = (double)numericUpDownImprintingBonusTester.Value / 100;
            }

            var levelStep = _creatureCollection.getWildLevelStep();
            Creature creature = new Creature(species, input.CreatureName, input.CreatureOwner, input.CreatureTribe, input.CreatureSex, GetCurrentWildLevels(fromExtractor), GetCurrentDomLevels(fromExtractor), te, bred, imprinting, levelStep: levelStep)
            {
                // set parents
                Mother = input.Mother,
                Father = input.Father,

                // cooldown-, growing-time
                cooldownUntil = input.CooldownUntil,
                growingUntil = input.GrowingUntil,

                flags = input.CreatureFlags,
                note = input.CreatureNote,
                server = input.CreatureServer,

                domesticatedAt = input.DomesticatedAt.HasValue && input.DomesticatedAt.Value.Year > 2014 ? input.DomesticatedAt.Value : default(DateTime?),
                addedToLibrary = DateTime.Now,
                mutationsMaternal = input.MutationCounterMother,
                mutationsPaternal = input.MutationCounterFather,
                Status = input.CreatureStatus,
                colors = input.RegionColors,
                guid = fromExtractor && input.CreatureGuid != Guid.Empty ? input.CreatureGuid : Guid.NewGuid(),
                ArkId = input.ArkId
            };

            creature.ArkIdImported = Utils.IsArkIdImported(creature.ArkId, creature.guid);
            creature.InitializeArkInGame();

            // parent guids
            if (motherArkId != 0)
                creature.motherGuid = Utils.ConvertArkIdToGuid(motherArkId);
            else if (input.MotherArkId != 0)
                creature.motherGuid = Utils.ConvertArkIdToGuid(input.MotherArkId);
            if (fatherArkId != 0)
                creature.fatherGuid = Utils.ConvertArkIdToGuid(fatherArkId);
            else if (input.FatherArkId != 0)
                creature.fatherGuid = Utils.ConvertArkIdToGuid(input.FatherArkId);

            // if creature is placeholder: add it
            // if creature's ArkId is already in library, suggest updating of the creature
            //if (!IsArkIdUniqueOrOnlyPlaceHolder(creature))
            //{
            //    // if creature is already in library, suggest updating or dismissing

            //    //ShowDuplicateMergerAndCheckForDuplicates()

            //    return;
            //}

            creature.RecalculateCreatureValues(levelStep);
            creature.RecalculateNewMutations();

            if (_creatureCollection.DeletedCreatureGuids != null
                && _creatureCollection.DeletedCreatureGuids.Contains(creature.guid))
                _creatureCollection.DeletedCreatureGuids.RemoveAll(guid => guid == creature.guid);

            _creatureCollection.MergeCreatureList(new List<Creature> { creature });

            // set status of exportedCreatureControl if available
            _exportedCreatureControl?.setStatus(importExported.ExportedCreatureControl.ImportStatus.JustImported, DateTime.Now);

            // if creature already exists by guid, use the already existing creature object for the parent assignments
            creature = _creatureCollection.creatures.SingleOrDefault(c => c.guid == creature.guid) ?? creature;

            // if new creature is parent of existing creatures, update link
            var motherOf = _creatureCollection.creatures.Where(c => c.motherGuid == creature.guid).ToList();
            foreach (Creature c in motherOf)
                c.Mother = creature;
            var fatherOf = _creatureCollection.creatures.Where(c => c.fatherGuid == creature.guid).ToList();
            foreach (Creature c in fatherOf)
                c.Father = creature;

            // if the new creature is the ancestor of any other creatures, update the generation count of all creatures
            if (motherOf.Any() || fatherOf.Any())
            {
                var creaturesOfSpecies = _creatureCollection.creatures.Where(c => c.Species == c.Species).ToList();
                foreach (var cr in creaturesOfSpecies) cr.generation = -1;
                foreach (var cr in creaturesOfSpecies) cr.RecalculateAncestorGenerations();
            }
            else
            {
                creature.RecalculateAncestorGenerations();
            }

            // link new creature to its parents if they're available, or creature placeholders
            if (creature.Mother == null || creature.Father == null)
                UpdateParents(new List<Creature> { creature });

            if (Properties.Settings.Default.PauseGrowingTimerAfterAddingBaby)
                creature.StartStopMatureTimer(false);

            _filterListAllowed = false;
            UpdateCreatureListings(species, false);

            // show only the added creatures' species
            listBoxSpeciesLib.SelectedItem = creature.Species;
            _filterListAllowed = true;
            _libraryNeedsUpdate = true;

            if (goToLibraryTab)
            {
                tabControlMain.SelectedTab = tabPageLibrary;

                // select new creature and ensure visibility
                _reactOnCreatureSelectionChange = false;
                listViewLibrary.SelectedItems.Clear();
                _reactOnCreatureSelectionChange = true;
                for (int i = 0; i < listViewLibrary.Items.Count; i++)
                {
                    if (creature == (Creature)listViewLibrary.Items[i].Tag)
                    {
                        listViewLibrary.Items[i].Focused = true;
                        listViewLibrary.Items[i].Selected = true;
                        listViewLibrary.EnsureVisible(i);
                        break;
                    }
                }
            }

            creatureInfoInputExtractor.parentListValid = false;
            creatureInfoInputTester.parentListValid = false;

            SetCollectionChanged(true, species);
            return creature;
        }

        /// <summary>
        /// Deletes the creatures selected in the library after a confirmation.
        /// </summary>
        private void DeleteSelectedCreatures()
        {
            if (tabControlMain.SelectedTab == tabPageLibrary)
            {
                if (listViewLibrary.SelectedItems.Count > 0)
                {
                    if (MessageBox.Show("Do you really want to delete the entry and all data for " +
                            $"\"{((Creature)listViewLibrary.SelectedItems[0].Tag).name}\"" +
                            $"{(listViewLibrary.SelectedItems.Count > 1 ? " and " + (listViewLibrary.SelectedItems.Count - 1) + " other creatures" : string.Empty)}?",
                            "Delete Creature?", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        bool onlyOneSpecies = true;
                        Species species = ((Creature)listViewLibrary.SelectedItems[0].Tag).Species;
                        foreach (ListViewItem i in listViewLibrary.SelectedItems)
                        {
                            if (onlyOneSpecies)
                            {
                                if (species != ((Creature)i.Tag).Species)
                                    onlyOneSpecies = false;
                            }
                            _creatureCollection.DeleteCreature((Creature)i.Tag);
                        }
                        _creatureCollection.RemoveUnlinkedPlaceholders();
                        UpdateCreatureListings(onlyOneSpecies ? species : null);
                        SetCollectionChanged(true, onlyOneSpecies ? species : null);
                    }
                }
            }
            else if (tabControlMain.SelectedTab == tabPagePlayerTribes)
            {
                tribesControl1.RemoveSelected();
            }
        }

        /// <summary>
        /// Checks if the ArkId of the given creature is already in the collection. If a placeholder has this id, the placeholder is removed and the placeholder.Guid is set to the creature.
        /// </summary>
        /// <param name="creature">Creature whose ArkId will be checked</param>
        /// <returns>True if the ArkId is unique (or only a placeholder had it). False if there is a conflict.</returns>
        private bool IsArkIdUniqueOrOnlyPlaceHolder(Creature creature)
        {
            bool arkIdIsUnique = true;

            if (creature.ArkId != 0 && _creatureCollection.ArkIdAlreadyExist(creature.ArkId, creature, out Creature guidCreature))
            {
                // if the creature is a placeholder replace the placeholder with the real creature
                if (guidCreature.flags.HasFlag(CreatureFlags.Placeholder) && creature.sex == guidCreature.sex && creature.Species == guidCreature.Species)
                {
                    // remove placeholder-creature from collection (is replaced by new creature)
                    _creatureCollection.creatures.Remove(guidCreature);
                }
                else
                {
                    // creature is not a placeholder, warn about id-conflict and don't add creature.
                    // TODO offer merging of the two creatures if they are similar (e.g. same species). merge automatically if only the dom-levels are different?
                    MessageBox.Show("The entered ARK-ID is already existing in this library " +
                            $"({guidCreature.Species.name} (lvl {guidCreature.Level}): {guidCreature.name}).\n" +
                            "You have to choose a different ARK-ID or delete the other creature first.",
                            "ARK-ID already existing",
                            MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    arkIdIsUnique = false;
                }
            }

            return arkIdIsUnique;
        }

        /// <summary>
        /// Returns the wild levels from the extractor or tester in an array.
        /// </summary>
        /// <param name="fromExtractor"></param>
        /// <returns></returns>
        private int[] GetCurrentWildLevels(bool fromExtractor = true)
        {
            int[] levelsWild = new int[Values.STATS_COUNT];
            for (int s = 0; s < Values.STATS_COUNT; s++)
            {
                levelsWild[s] = fromExtractor ? _statIOs[s].LevelWild : _testingIOs[s].LevelWild;
            }
            return levelsWild;
        }

        /// <summary>
        /// Returns the domesticated levels from the extractor or tester in an array.
        /// </summary>
        /// <param name="fromExtractor"></param>
        /// <returns></returns>
        private int[] GetCurrentDomLevels(bool fromExtractor = true)
        {
            int[] levelsDom = new int[Values.STATS_COUNT];
            for (int s = 0; s < Values.STATS_COUNT; s++)
            {
                levelsDom[s] = fromExtractor ? _statIOs[s].LevelDom : _testingIOs[s].LevelDom;
            }
            return levelsDom;
        }

        /// <summary>
        /// Call after the creatureCollection-object was created anew (e.g. after loading a file)
        /// </summary>
        /// <param name="keepCurrentSelection">True if synchronized library file is loaded.</param>
        private void InitializeCollection(bool keepCurrentSelection = false)
        {
            // set pointer to current collection
            CreatureCollection.CurrentCreatureCollection = _creatureCollection;
            pedigree1.SetCreatures(_creatureCollection.creatures);
            breedingPlan1.CreatureCollection = _creatureCollection;
            tribesControl1.Tribes = _creatureCollection.tribes;
            tribesControl1.Players = _creatureCollection.players;
            timerList1.CreatureCollection = _creatureCollection;
            notesControl1.NoteList = _creatureCollection.noteList;
            raisingControl1.CreatureCollection = _creatureCollection;
            statsMultiplierTesting1.CreatureCollection = _creatureCollection;

            UpdateParents(_creatureCollection.creatures);
            UpdateIncubationParents(_creatureCollection);

            CreateCreatureTagList();

            if (_creatureCollection.modIDs == null) _creatureCollection.modIDs = new List<string>();

            if (keepCurrentSelection)
            {
                pedigree1.RecreateAfterLoading(tabControlMain.SelectedTab == tabPagePedigree);
                breedingPlan1.RecreateAfterLoading(tabControlMain.SelectedTab == tabPageBreedingPlan);
            }
            else
            {
                pedigree1.Clear();
                breedingPlan1.Clear();
            }

            ApplySpeciesObjectsToCollection(_creatureCollection);

            UpdateTempCreatureDropDown();
        }

        /// <summary>
        /// Applies the species object to the creatures and creatureValues of the collection.
        /// </summary>
        /// <param name="cc"></param>
        private static void ApplySpeciesObjectsToCollection(CreatureCollection cc)
        {
            foreach (var cr in cc.creatures)
            {
                cr.Species = Values.V.SpeciesByBlueprint(cr.speciesBlueprint);
            }
            foreach (var cv in cc.creaturesValues)
            {
                cv.Species = Values.V.SpeciesByBlueprint(cv.speciesBlueprint);
            }
        }

        /// <summary>
        /// Calculates the top-stats in each species, sets the top-stat-flags in the creatures
        /// </summary>
        /// <param name="creatures">creatures to consider</param>
        private void CalculateTopStats(List<Creature> creatures)
        {
            var filteredCreaturesHash = Properties.Settings.Default.useFiltersInTopStatCalculation ? new HashSet<Creature>(ApplyLibraryFilterSettings(creatures)) : null;

            var speciesCreaturesGroups = creatures.GroupBy(c => c.Species);

            foreach (var g in speciesCreaturesGroups)
            {
                var species = g.Key;
                if (species == null)
                    continue;
                var speciesCreatures = g.ToArray();

                List<int> usedStatIndices = new List<int>(Values.STATS_COUNT);
                List<int> usedAndConsideredStatIndices = new List<int>(Values.STATS_COUNT);
                int[] bestStat = new int[Values.STATS_COUNT];
                int[] lowestStat = new int[Values.STATS_COUNT];
                for (int s = 0; s < Values.STATS_COUNT; s++)
                {
                    bestStat[s] = -1;
                    lowestStat[s] = -1;
                    if (species.UsesStat(s))
                    {
                        usedStatIndices.Add(s);
                        if (_considerStatHighlight[s])
                            usedAndConsideredStatIndices.Add(s);
                    }
                }
                List<Creature>[] bestCreatures = new List<Creature>[Values.STATS_COUNT];
                int usedStatsCount = usedStatIndices.Count;
                int usedAndConsideredStatsCount = usedAndConsideredStatIndices.Count;

                foreach (var c in speciesCreatures)
                {
                    // reset topBreeding stats for this creature
                    c.topBreedingStats = new bool[Values.STATS_COUNT];
                    c.topBreedingCreature = false;

                    if (
                        //if not in the filtered collection (using library filter settings), continue
                        (filteredCreaturesHash != null && !filteredCreaturesHash.Contains(c))
                        // only consider creature if it's available for breeding
                        || !(c.Status == CreatureStatus.Available
                            || c.Status == CreatureStatus.Cryopod
                            || c.Status == CreatureStatus.Obelisk
                            )
                        )
                    {
                        continue;
                    }

                    for (int s = 0; s < usedStatsCount; s++)
                    {
                        int si = usedStatIndices[s];
                        if (c.levelsWild[si] != -1 && (lowestStat[si] == -1 || c.levelsWild[si] < lowestStat[si]))
                        {
                            lowestStat[si] = c.levelsWild[si];
                        }

                        if (c.levelsWild[si] <= 0) continue;

                        if (c.levelsWild[si] == bestStat[si])
                        {
                            bestCreatures[si].Add(c);
                        }
                        else if (c.levelsWild[si] > bestStat[si])
                        {
                            bestCreatures[si] = new List<Creature> { c };
                            bestStat[si] = c.levelsWild[si];
                        }
                    }
                }

                if (!_topLevels.ContainsKey(species))
                {
                    _topLevels.Add(species, bestStat);
                }
                else
                {
                    _topLevels[species] = bestStat;
                }

                if (!_lowestLevels.ContainsKey(species))
                {
                    _lowestLevels.Add(species, lowestStat);
                }
                else
                {
                    _lowestLevels[species] = lowestStat;
                }

                // bestStat and bestCreatures now contain the best stats and creatures for each stat.

                // set topness of each creature (== mean wildLevels/mean top wildLevels in permille)
                int sumTopLevels = 0;
                for (int s = 0; s < usedAndConsideredStatsCount; s++)
                {
                    int si = usedAndConsideredStatIndices[s];
                    if (bestStat[si] > 0)
                        sumTopLevels += bestStat[si];
                }
                if (sumTopLevels > 0)
                {
                    foreach (var c in speciesCreatures)
                    {
                        int sumCreatureLevels = 0;
                        for (int s = 0; s < usedAndConsideredStatsCount; s++)
                        {
                            int si = usedAndConsideredStatIndices[s];
                            sumCreatureLevels += c.levelsWild[si] > 0 ? c.levelsWild[si] : 0;
                        }
                        c.topness = (short)(1000 * sumCreatureLevels / sumTopLevels);
                    }
                }

                // if any male is in more than 1 category, remove any male from the topBreedingCreatures that is not top in at least 2 categories himself
                for (int s = 0; s < Values.STATS_COUNT; s++)
                {
                    if (bestCreatures[s] == null || bestCreatures[s].Count == 0)
                    {
                        continue; // no creature has levelups in this stat or the stat is not used for this species
                    }

                    var crCount = bestCreatures[s].Count;
                    if (crCount == 1)
                    {
                        bestCreatures[s][0].topBreedingCreature = true;
                        continue;
                    }

                    for (int c = 0; c < crCount; c++)
                    {
                        bestCreatures[s][c].topBreedingCreature = true;
                        if (bestCreatures[s][c].sex != Sex.Male)
                            continue;

                        Creature currentCreature = bestCreatures[s][c];
                        // check how many best stat the male has
                        int maxval = 0;
                        for (int cs = 0; cs < Values.STATS_COUNT; cs++)
                        {
                            if (currentCreature.levelsWild[cs] == bestStat[cs])
                                maxval++;
                        }

                        if (maxval > 1)
                        {
                            // check now if the other males have only 1.
                            for (int oc = 0; oc < crCount; oc++)
                            {
                                if (bestCreatures[s][oc].sex != Sex.Male)
                                    continue;

                                if (oc == c)
                                    continue;

                                Creature otherMale = bestCreatures[s][oc];

                                int othermaxval = 0;
                                for (int ocs = 0; ocs < Values.STATS_COUNT; ocs++)
                                {
                                    if (otherMale.levelsWild[ocs] == bestStat[ocs])
                                        othermaxval++;
                                }
                                if (othermaxval == 1)
                                    bestCreatures[s][oc].topBreedingCreature = false;
                            }
                        }
                    }
                }

                // now we have a list of all candidates for breeding. Iterate on stats.
                for (int s = 0; s < Values.STATS_COUNT; s++)
                {
                    if (bestCreatures[s] != null)
                    {
                        for (int c = 0; c < bestCreatures[s].Count; c++)
                        {
                            // flag topStats in creatures
                            bestCreatures[s][c].topBreedingStats[s] = true;
                        }
                    }
                }
            }

            bool considerWastedStatsForTopCreatures = Properties.Settings.Default.ConsiderWastedStatsForTopCreatures;
            foreach (Creature c in creatures)
                c.SetTopStatCount(_considerStatHighlight, considerWastedStatsForTopCreatures);
        }

        /// <summary>
        /// Sets the parents according to the guids. Call after a file is loaded.
        /// </summary>
        /// <param name="creatures"></param>
        private void UpdateParents(IEnumerable<Creature> creatures)
        {
            List<Creature> placeholderAncestors = new List<Creature>();

            var creatureGuids = _creatureCollection.creatures.ToDictionary(c => c.guid);

            foreach (Creature c in creatures)
            {
                if (c.motherGuid == Guid.Empty && c.fatherGuid == Guid.Empty) continue;

                Creature mother = null;
                if (c.motherGuid == Guid.Empty
                    || !creatureGuids.TryGetValue(c.motherGuid, out mother))
                    mother = EnsurePlaceholderCreature(placeholderAncestors, c, c.motherArkId, c.motherGuid, c.motherName, Sex.Female);

                Creature father = null;
                if (c.fatherGuid == Guid.Empty
                    || !creatureGuids.TryGetValue(c.fatherGuid, out father))
                    father = EnsurePlaceholderCreature(placeholderAncestors, c, c.fatherArkId, c.fatherGuid, c.fatherName, Sex.Male);

                c.Mother = mother;
                c.Father = father;
            }

            _creatureCollection.creatures.AddRange(placeholderAncestors);
        }

        /// <summary>
        /// Ensures the given placeholder ancestor exists in the list of placeholders.
        /// Does nothing when the details are not well specified.
        /// </summary>
        /// <param name="placeholders">List of placeholders to amend</param>
        /// <param name="tmpl">Descendant creature to use as a template</param>
        /// <param name="arkId">ArkId of creature to create. Only pass this if it's from an import</param>
        /// <param name="guid">GUID of creature to create</param>
        /// <param name="name">Name of the creature to create</param>
        /// <param name="sex">Sex of the creature to create</param>
        /// <returns></returns>
        private Creature EnsurePlaceholderCreature(List<Creature> placeholders, Creature tmpl, long arkId, Guid guid, string name, Sex sex)
        {
            if (guid == Guid.Empty && arkId == 0)
                return null;
            var existing = placeholders.SingleOrDefault(ph => ph.guid == guid);
            if (existing != null)
                return existing;

            if (string.IsNullOrEmpty(name))
                name = (sex == Sex.Female ? "Mother" : "Father") + " of " + tmpl.name;

            Guid creatureGuid = arkId != 0 ? Utils.ConvertArkIdToGuid(arkId) : guid;
            var creature = new Creature(tmpl.Species, name, tmpl.owner, tmpl.tribe, sex, new[] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
                    levelStep: _creatureCollection.getWildLevelStep())
            {
                guid = creatureGuid,
                Status = CreatureStatus.Unavailable,
                flags = CreatureFlags.Placeholder,
                ArkId = arkId,
                ArkIdImported = Utils.IsArkIdImported(arkId, creatureGuid)
            };

            placeholders.Add(creature);

            return creature;
        }

        /// <summary>
        /// Sets the parents of the incubation-timers according to the guids. Call after a file is loaded.
        /// </summary>
        /// <param name="cc"></param>
        private void UpdateIncubationParents(CreatureCollection cc)
        {
            foreach (Creature c in cc.creatures)
            {
                if (c.guid != Guid.Empty)
                {
                    foreach (IncubationTimerEntry it in cc.incubationListEntries)
                    {
                        if (c.guid == it.motherGuid)
                            it.mother = c;
                        else if (c.guid == it.fatherGuid)
                            it.father = c;
                    }
                }
            }
        }

        private void ShowCreaturesInListView(IEnumerable<Creature> creatures)
        {
            listViewLibrary.BeginUpdate();

            // clear ListView
            listViewLibrary.Items.Clear();
            listViewLibrary.Groups.Clear();

            Dictionary<string, ListViewGroup> speciesGroups = new Dictionary<string, ListViewGroup>();
            List<ListViewItem> items = new List<ListViewItem>();

            foreach (Creature cr in creatures)
            {
                // if species is unknown, don't display the creature
                if (cr.Species == null)
                    continue;

                // check if group of species exists
                var spDesc = cr.Species.DescriptiveNameAndMod;
                if (!speciesGroups.TryGetValue(spDesc, out var group))
                {
                    group = new ListViewGroup(spDesc);
                    speciesGroups.Add(spDesc, group);
                }

                items.Add(CreateCreatureLVItem(cr, group));
            }
            // use species list as initial source to get the sorted order set by the user
            listViewLibrary.Groups.AddRange(Values.V.species.Select(sp => sp.DescriptiveNameAndMod).Where(sp => speciesGroups.ContainsKey(sp)).Select(sp => speciesGroups[sp]).ToArray());
            listViewLibrary.Items.AddRange(items.ToArray());
            listViewLibrary.EndUpdate();

            // highlight filter input if something is entered and no results are available
            if (string.IsNullOrEmpty(ToolStripTextBoxLibraryFilter.Text))
            {
                ToolStripTextBoxLibraryFilter.BackColor = SystemColors.Window;
                ToolStripButtonLibraryFilterClear.BackColor = SystemColors.Control;
            }
            else
            {
                // if no items are shown, shade red, if something is shown and potentially some are sorted out, shade yellow
                ToolStripTextBoxLibraryFilter.BackColor = items.Any() ? Color.LightGoldenrodYellow : Color.LightSalmon;
                ToolStripButtonLibraryFilterClear.BackColor = Color.Orange;
            }
        }

        /// <summary>
        /// Call this function to update the displayed values of a creature. Usually called after a creature was edited.
        /// </summary>
        /// <param name="cr">Creature that was changed</param>
        /// <param name="creatureStatusChanged"></param>
        private void UpdateDisplayedCreatureValues(Creature cr, bool creatureStatusChanged, bool ownerServerChanged)
        {
            _reactOnCreatureSelectionChange = false;
            // if row is selected, save and reselect later
            List<Creature> selectedCreatures = new List<Creature>();
            foreach (ListViewItem i in listViewLibrary.SelectedItems)
                selectedCreatures.Add((Creature)i.Tag);

            // data of the selected creature changed, update listview
            cr.RecalculateCreatureValues(_creatureCollection.getWildLevelStep());
            // if creatureStatus (available/dead) changed, recalculate topStats (dead creatures are not considered there)
            if (creatureStatusChanged)
            {
                CalculateTopStats(_creatureCollection.creatures.Where(c => c.Species == cr.Species).ToList());
                FilterLibRecalculate();
                UpdateStatusBar();
            }
            else
            {
                // int listViewLibrary replace old row with new one
                int ci = -1;
                for (int i = 0; i < listViewLibrary.Items.Count; i++)
                {
                    if ((Creature)listViewLibrary.Items[i].Tag == cr)
                    {
                        ci = i;
                        break;
                    }
                }
                if (ci >= 0)
                    listViewLibrary.Items[ci] = CreateCreatureLVItem(cr, listViewLibrary.Items[ci].Group);
            }

            // recreate ownerList
            if (ownerServerChanged)
                UpdateOwnerServerTagLists();
            SetCollectionChanged(true, cr.Species);

            // select previous selected creatures again
            int selectedCount = selectedCreatures.Count;
            if (selectedCount > 0)
            {
                for (int i = 0; i < listViewLibrary.Items.Count; i++)
                {
                    if (selectedCreatures.Contains((Creature)listViewLibrary.Items[i].Tag))
                    {
                        listViewLibrary.Items[i].Focused = true;
                        listViewLibrary.Items[i].Selected = true;
                        if (--selectedCount == 0)
                        {
                            listViewLibrary.EnsureVisible(i);
                            break;
                        }
                    }
                }
            }
            _reactOnCreatureSelectionChange = true;
        }

        private ListViewItem CreateCreatureLVItem(Creature cr, ListViewGroup g)
        {
            double colorFactor = 100d / _creatureCollection.maxChartLevel;
            DateTime? cldGr = cr.cooldownUntil.HasValue && cr.growingUntil.HasValue ?
                (cr.cooldownUntil.Value > cr.growingUntil.Value ? cr.cooldownUntil.Value : cr.growingUntil.Value)
                : cr.cooldownUntil ?? cr.growingUntil;

            string[] subItems = new[]
                    {
                            cr.name,
                            cr.owner,
                            cr.note,
                            cr.server,
                            Utils.SexSymbol(cr.sex),
                            cr.domesticatedAt?.ToString("yyyy'-'MM'-'dd HH':'mm':'ss") ?? string.Empty,
                            (cr.topness / 10).ToString(),
                            cr.topStatsCount.ToString(),
                            cr.generation.ToString(),
                            cr.levelFound.ToString(),
                            cr.Mutations.ToString(),
                            DisplayedCreatureCountdown(cr, out var cooldownForeColor, out var cooldownBackColor)
                    }
                    .Concat(cr.levelsWild.Select(x => x.ToString()).ToArray())
                    .ToArray();

            if (Properties.Settings.Default.showColorsInLibrary)
                subItems = subItems.Concat(cr.colors.Select(cl => cl.ToString()).ToArray()).ToArray();
            else
                subItems = subItems.Concat(new string[6]).ToArray();

            // add the species and status and tribe
            subItems = subItems.Concat(new[] {
                cr.Species.DescriptiveNameAndMod,
                cr.Status.ToString(),
                cr.tribe,
                Utils.StatusSymbol(cr.Status, string.Empty)
            }).ToArray();

            // check if we display group for species or not.
            ListViewItem lvi = Properties.Settings.Default.LibraryGroupBySpecies ? new ListViewItem(subItems, g) : new ListViewItem(subItems);

            for (int s = 0; s < Values.STATS_COUNT; s++)
            {
                if (cr.valuesDom[s] == 0)
                {
                    // not used
                    lvi.SubItems[s + 12].ForeColor = Color.White;
                    lvi.SubItems[s + 12].BackColor = Color.White;
                }
                else if (cr.levelsWild[s] < 0)
                {
                    // unknown level 
                    lvi.SubItems[s + 12].ForeColor = Color.WhiteSmoke;
                    lvi.SubItems[s + 12].BackColor = Color.White;
                }
                else
                    lvi.SubItems[s + 12].BackColor = Utils.GetColorFromPercent((int)(cr.levelsWild[s] * (s == (int)StatNames.Torpidity ? colorFactor / 7 : colorFactor)), // TODO set factor to number of other stats (flyers have 6, Gacha has 8?)
                            _considerStatHighlight[s] ? cr.topBreedingStats[s] ? 0.2 : 0.7 : 0.93);
            }
            lvi.SubItems[4].BackColor = cr.flags.HasFlag(CreatureFlags.Neutered) ? Color.FromArgb(220, 220, 220) :
                    cr.sex == Sex.Female ? Color.FromArgb(255, 230, 255) :
                    cr.sex == Sex.Male ? Color.FromArgb(220, 235, 255) : SystemColors.Window;

            if (cr.Status == CreatureStatus.Dead)
            {
                lvi.SubItems[0].ForeColor = SystemColors.GrayText;
                lvi.BackColor = Color.FromArgb(255, 250, 240);
            }
            else if (cr.Status == CreatureStatus.Unavailable)
            {
                lvi.SubItems[0].ForeColor = SystemColors.GrayText;
            }
            else if (cr.Status == CreatureStatus.Obelisk)
            {
                lvi.SubItems[0].ForeColor = Color.DarkBlue;
            }
            else if (_creatureCollection.maxServerLevel > 0
                    && cr.levelsWild[(int)StatNames.Torpidity] + 1 + _creatureCollection.maxDomLevel > _creatureCollection.maxServerLevel + (cr.Species.name.StartsWith("X-") ? 50 : 0))
            {
                lvi.SubItems[0].ForeColor = Color.OrangeRed; // this creature may pass the max server level and could be deleted by the game
            }

            lvi.UseItemStyleForSubItems = false;

            // color for top-stats-nr
            if (cr.topStatsCount > 0)
            {
                if (Properties.Settings.Default.LibraryHighlightTopCreatures && cr.topBreedingCreature)
                {
                    if (cr.onlyTopConsideredStats)
                        lvi.BackColor = Color.Gold;
                    else
                        lvi.BackColor = Color.LightGreen;
                }
                lvi.SubItems[7].BackColor = Utils.GetColorFromPercent(cr.topStatsCount * 8 + 44, 0.7);
            }
            else
            {
                lvi.SubItems[7].ForeColor = Color.LightGray;
            }

            // color for timestamp domesticated
            if (cr.domesticatedAt == null || cr.domesticatedAt.Value.Year < 2015)
            {
                lvi.SubItems[5].Text = "n/a";
                lvi.SubItems[5].ForeColor = Color.LightGray;
            }

            // color for topness
            lvi.SubItems[6].BackColor = Utils.GetColorFromPercent(cr.topness / 5 - 100, 0.8); // topness is in permille. gradient from 50-100

            // color for generation
            if (cr.generation == 0)
                lvi.SubItems[8].ForeColor = Color.LightGray;

            // color of WildLevelColumn
            if (cr.levelFound == 0)
                lvi.SubItems[9].ForeColor = Color.LightGray;

            // color for mutation
            if (cr.Mutations > 0)
            {
                if (cr.Mutations > 19)
                    lvi.SubItems[10].BackColor = Utils.MutationColorOverLimit;
                else
                    lvi.SubItems[10].BackColor = Utils.MutationColor;
            }
            else
                lvi.SubItems[10].ForeColor = Color.LightGray;

            // color for cooldown
            lvi.SubItems[11].ForeColor = cooldownForeColor;
            lvi.SubItems[11].BackColor = cooldownBackColor;

            if (Properties.Settings.Default.showColorsInLibrary)
            {
                // color for colors
                for (int cl = 0; cl < 6; cl++)
                {
                    if (cr.colors[cl] != 0)
                    {
                        lvi.SubItems[24 + cl].BackColor = CreatureColors.CreatureColor(cr.colors[cl]);
                        lvi.SubItems[24 + cl].ForeColor = Utils.ForeColor(lvi.SubItems[24 + cl].BackColor);
                    }
                    else
                    {
                        lvi.SubItems[24 + cl].ForeColor = cr.Species.EnabledColorRegions[cl] ? Color.LightGray : Color.White;
                    }
                }
            }

            lvi.Tag = cr;
            return lvi;
        }

        /// <summary>
        /// Returns the dateTime when the countdown of a creature is ready. Either the maturingTime, the matingCooldownTime or null if no countdown is set.
        /// </summary>
        /// <returns></returns>
        private string DisplayedCreatureCountdown(Creature cr, out Color foreColor, out Color backColor)
        {
            foreColor = SystemColors.ControlText;
            backColor = SystemColors.Window;
            DateTime dt;
            var isGrowing = true;
            var useGrowingLeft = false;
            var now = DateTime.Now;
            if (cr.cooldownUntil.HasValue && cr.cooldownUntil.Value > now)
            {
                isGrowing = false;
                dt = cr.cooldownUntil.Value;
            }
            else if (!cr.growingUntil.HasValue)
            {
                foreColor = Color.LightGray;
                return "-";
            }
            else if (!cr.growingPaused)
            {
                dt = cr.growingUntil.Value;
            }
            else
            {
                useGrowingLeft = true;
                dt = new DateTime();
            }

            if (!useGrowingLeft && now > dt)
            {
                foreColor = Color.LightGray;
                return "-";
            }

            double minCld;
            if (useGrowingLeft)
                minCld = cr.growingLeft.TotalMinutes;
            else
                minCld = dt.Subtract(now).TotalMinutes;

            if (isGrowing)
            {
                // growing
                if (minCld < 1)
                    backColor = Color.FromArgb(168, 187, 255); // light blue
                else if (minCld < 10)
                    backColor = Color.FromArgb(197, 168, 255); // light blue/pink
                else
                    backColor = Color.FromArgb(236, 168, 255); // light pink
            }
            else
            {
                // mating-cooldown
                if (minCld < 1)
                    backColor = Color.FromArgb(235, 255, 109); // green-yellow
                else if (minCld < 10)
                    backColor = Color.FromArgb(255, 250, 109); // yellow
                else
                    backColor = Color.FromArgb(255, 179, 109); // yellow-orange
            }

            return useGrowingLeft ? Utils.Duration(cr.growingLeft) : dt.ToString();
        }

        private void listView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            ListViewColumnSorter.DoSort((ListView)sender, e.Column);
        }

        private Debouncer libraryIndexChangedDebouncer = new Debouncer();

        // onlibrarychange
        private void listViewLibrary_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_reactOnCreatureSelectionChange)
                libraryIndexChangedDebouncer.Debounce(100, LibrarySelectedIndexChanged, Dispatcher.CurrentDispatcher);
        }

        /// <summary>
        /// Updates infos about the selected creatures like tags, levels and stat-level distribution.
        /// </summary>
        private void LibrarySelectedIndexChanged()
        {
            int cnt = listViewLibrary.SelectedItems.Count;
            if (cnt == 0)
            {
                SetMessageLabelText();
                creatureBoxListView.Clear();
                return;
            }

            if (cnt == 1)
            {
                Creature c = (Creature)listViewLibrary.SelectedItems[0].Tag;
                creatureBoxListView.SetCreature(c);
                if (tabControlLibFilter.SelectedTab == tabPageLibRadarChart)
                    radarChartLibrary.SetLevels(c.levelsWild);
                pedigree1.PedigreeNeedsUpdate = true;
            }

            // display infos about the selected creatures
            List<Creature> selCrs = new List<Creature>();
            for (int i = 0; i < cnt; i++)
                selCrs.Add((Creature)listViewLibrary.SelectedItems[i].Tag);

            List<string> tagList = new List<string>();
            foreach (Creature cr in selCrs)
            {
                foreach (string t in cr.tags)
                    if (!tagList.Contains(t))
                        tagList.Add(t);
            }
            tagList.Sort();

            SetMessageLabelText($"{cnt} creatures selected, " +
                    $"{selCrs.Count(cr => cr.sex == Sex.Female)} females, " +
                    $"{selCrs.Count(cr => cr.sex == Sex.Male)} males\n" +
                    (cnt == 1
                        ? $"level: {selCrs[0].Level}; Ark-Id (ingame): " + (selCrs[0].ArkIdImported ? Utils.ConvertImportedArkIdToIngameVisualization(selCrs[0].ArkId) : selCrs[0].ArkId.ToString())
                        : $"level-range: {selCrs.Min(cr => cr.Level)} - {selCrs.Max(cr => cr.Level)}"
                    ) + "\n" +
                    $"Tags: {string.Join(", ", tagList)}");
        }

        /// <summary>
        /// Display the creatures with the current filter.
        /// Recalculate all filters.
        /// </summary>
        private void FilterLibRecalculate()
        {
            _creaturesPreFiltered = null;
            FilterLib();
        }

        /// <summary>
        /// Display the creatures with the current filter.
        /// Use the pre filtered list (if available) and only apply the live filter.
        /// </summary>
        private void FilterLib()
        {
            if (!_filterListAllowed)
                return;

            // save selected creatures to re-select them after the filtering
            List<Creature> selectedCreatures = new List<Creature>();
            foreach (ListViewItem i in listViewLibrary.SelectedItems)
                selectedCreatures.Add((Creature)i.Tag);

            IEnumerable<Creature> filteredList;

            if (_creaturesPreFiltered == null)
            {
                filteredList = from creature in _creatureCollection.creatures
                               where !creature.flags.HasFlag(CreatureFlags.Placeholder)
                               select creature;

                // if only one species should be shown adjust headers if the selected species has custom statNames
                Dictionary<string, string> customStatNames = null;
                if (listBoxSpeciesLib.SelectedItem is Species selectedSpecies)
                {
                    filteredList = filteredList.Where(c => c.Species == selectedSpecies);
                    customStatNames = selectedSpecies.statNames;
                }

                for (int s = 0; s < Values.STATS_COUNT; s++)
                    listViewLibrary.Columns[12 + s].Text = Utils.StatName(s, true, customStatNames);

                _creaturesPreFiltered = ApplyLibraryFilterSettings(filteredList).ToArray();
            }

            filteredList = _creaturesPreFiltered;
            // apply live filter
            var filterString = ToolStripTextBoxLibraryFilter.Text.Trim();
            if (!string.IsNullOrEmpty(filterString))
            {
                // filter parameter are separated by commas and all parameter must be found on an item to have it included
                var filterStrings = filterString.Split(',').Select(f => f.Trim())
                    .Where(f => !string.IsNullOrEmpty(f)).ToList();

                // extract stat level filter
                var statGreaterThan = new Dictionary<int, int>();
                var statLessThan = new Dictionary<int, int>();
                var statEqualTo = new Dictionary<int, int>();
                var statFilterRegex = new Regex(@"(\w{2}) ?(<|>|==) ?(\d+)");

                // color filter
                var colorFilter = new Dictionary<int, int[]>();
                var colorFilterRegex = new Regex(@"c([0-5]): ?([\d ]+)");

                var removeFilterIndex = new List<int>();
                for (var i = filterStrings.Count - 1; i >= 0; i--)
                {
                    var f = filterStrings[i];

                    // color region filter
                    var m = colorFilterRegex.Match(f);
                    if (m.Success)
                    {
                        var colorRegion = int.Parse(m.Groups[1].Value);
                        if (colorFilter.ContainsKey(colorRegion)) continue;

                        var colorIds = m.Groups[2].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(cId => int.Parse(cId)).Distinct().ToArray();
                        if (!colorIds.Any()) continue;

                        colorFilter.Add(colorRegion, colorIds);
                        removeFilterIndex.Add(i);
                        continue;
                    }

                    // stat filter
                    m = statFilterRegex.Match(f);
                    if (!m.Success
                        || !Utils.StatAbbreviationToIndex.TryGetValue(m.Groups[1].Value, out var statIndex))
                        continue;

                    switch (m.Groups[2].Value)
                    {
                        case ">":
                            statGreaterThan.Add(statIndex, int.Parse(m.Groups[3].Value));
                            break;
                        case "<":
                            statLessThan.Add(statIndex, int.Parse(m.Groups[3].Value));
                            break;
                        case "==":
                            statEqualTo.Add(statIndex, int.Parse(m.Groups[3].Value));
                            break;
                    }
                    removeFilterIndex.Add(i);
                }

                if (!statGreaterThan.Any()) statGreaterThan = null;
                if (!statLessThan.Any()) statLessThan = null;
                if (!statEqualTo.Any()) statEqualTo = null;
                if (!colorFilter.Any()) colorFilter = null;
                foreach (var i in removeFilterIndex)
                    filterStrings.RemoveAt(i);

                filteredList = filteredList.Where(c => filterStrings.All(f =>
                    c.name.IndexOf(f, StringComparison.InvariantCultureIgnoreCase) != -1
                    || (c.Species?.name.IndexOf(f, StringComparison.InvariantCultureIgnoreCase) ?? -1) != -1
                    || (c.owner?.IndexOf(f, StringComparison.InvariantCultureIgnoreCase) ?? -1) != -1
                    || (c.tribe?.IndexOf(f, StringComparison.InvariantCultureIgnoreCase) ?? -1) != -1
                    || (c.note?.IndexOf(f, StringComparison.InvariantCultureIgnoreCase) ?? -1) != -1
                    || (c.ArkIdInGame?.StartsWith(f) ?? false)
                    || (c.server?.IndexOf(f, StringComparison.InvariantCultureIgnoreCase) ?? -1) != -1
                    || (c.tags?.Any(t => string.Equals(t, f, StringComparison.InvariantCultureIgnoreCase)) ?? false)
                )
                && (statGreaterThan?.All(si => c.levelsWild[si.Key] > si.Value) ?? true)
                && (statLessThan?.All(si => c.levelsWild[si.Key] < si.Value) ?? true)
                && (statEqualTo?.All(si => c.levelsWild[si.Key] == si.Value) ?? true)
                && (colorFilter?.All(cr => cr.Value.Contains(c.colors[cr.Key])) ?? true)
                );
            }

            // display new results
            ShowCreaturesInListView(filteredList);

            // update creatureBox
            creatureBoxListView.UpdateLabel();

            // select previous selected creatures again
            int selectedCount = selectedCreatures.Count;
            if (selectedCount > 0)
            {
                for (int i = 0; i < listViewLibrary.Items.Count; i++)
                {
                    if (selectedCreatures.Contains((Creature)listViewLibrary.Items[i].Tag))
                    {
                        listViewLibrary.Items[i].Selected = true;
                        if (--selectedCount == 0)
                        {
                            listViewLibrary.Items[i].Focused = true;
                            listViewLibrary.EnsureVisible(i);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Apply library filter settings to a creature collection
        /// </summary>
        private IEnumerable<Creature> ApplyLibraryFilterSettings(IEnumerable<Creature> creatures)
        {
            if (creatures == null)
                return Enumerable.Empty<Creature>();

            if (Properties.Settings.Default.FilterHideOwners?.Any() ?? false)
                creatures = creatures.Where(c => !Properties.Settings.Default.FilterHideOwners.Contains(c.owner ?? string.Empty));

            if (Properties.Settings.Default.FilterHideTribes?.Any() ?? false)
                creatures = creatures.Where(c => !Properties.Settings.Default.FilterHideTribes.Contains(c.tribe ?? string.Empty));

            if (Properties.Settings.Default.FilterHideServers?.Any() ?? false)
                creatures = creatures.Where(c => !Properties.Settings.Default.FilterHideServers.Contains(c.server ?? string.Empty));

            if (Properties.Settings.Default.FilterOnlyIfColorId != 0)
                creatures = creatures.Where(c => c.colors.Contains(Properties.Settings.Default.FilterOnlyIfColorId));

            // tags filter
            if (Properties.Settings.Default.FilterHideTags?.Any() ?? false)
            {
                bool hideCreaturesWOTags = Properties.Settings.Default.FilterHideTags.Contains(string.Empty);
                creatures = creatures.Where(c =>
                    !hideCreaturesWOTags && c.tags.Count == 0 ||
                    c.tags.Except(Properties.Settings.Default.FilterHideTags).Any());
            }

            // hide creatures with the set hide flags
            if (Properties.Settings.Default.FilterFlagsExclude != 0)
            {
                creatures = creatures.Where(c => ((int)c.flags & Properties.Settings.Default.FilterFlagsExclude) == 0);
            }
            if (Properties.Settings.Default.FilterFlagsAllNeeded != 0)
            {
                creatures = creatures.Where(c => ((int)c.flags & Properties.Settings.Default.FilterFlagsAllNeeded) == Properties.Settings.Default.FilterFlagsAllNeeded);
            }
            if (Properties.Settings.Default.FilterFlagsOneNeeded != 0)
            {
                int flagsOneNeeded = Properties.Settings.Default.FilterFlagsOneNeeded |
                                     Properties.Settings.Default.FilterFlagsAllNeeded;
                creatures = creatures.Where(c => ((int)c.flags & flagsOneNeeded) != 0);
            }

            return creatures;
        }

        private void listViewLibrary_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedCreatures();
            }
            else if (e.KeyCode == Keys.F2)
            {
                if (listViewLibrary.SelectedIndices.Count > 0)
                    EditCreatureInTester((Creature)listViewLibrary.Items[listViewLibrary.SelectedIndices[0]].Tag);
            }
            else if (e.KeyCode == Keys.F3)
            {
                if (listViewLibrary.SelectedIndices.Count > 0)
                    ShowMultiSetter();
            }
            else if (e.KeyCode == Keys.F5)
            {
                if (listViewLibrary.SelectedIndices.Count > 0)
                    AdminCommandToSetColors();
            }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                // select all list-entries
                _reactOnCreatureSelectionChange = false;
                listViewLibrary.BeginUpdate();
                foreach (ListViewItem i in listViewLibrary.Items)
                    i.Selected = true;
                listViewLibrary.EndUpdate();
                _reactOnCreatureSelectionChange = true;
                listViewLibrary_SelectedIndexChanged(null, null);
            }
            else if (e.KeyCode == Keys.B && e.Control)
            {
                CopySelectedCreatureName();
            }
        }

        /// <summary>
        /// Copies the data of the selected creatures to the clipboard for use in a spreadsheet.
        /// </summary>
        private void ExportForSpreadsheet()
        {
            if (tabControlMain.SelectedTab == tabPageLibrary)
            {
                if (listViewLibrary.SelectedItems.Count > 0)
                {
                    ExportCreatures.ExportTable(listViewLibrary.SelectedItems.Cast<ListViewItem>().Select(lvi => (Creature)lvi.Tag));
                }
                else
                    MessageBox.Show("No creatures in the library selected to copy to the clipboard", "No Creatures Selected",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (tabControlMain.SelectedTab == tabPageExtractor)
                CopyExtractionToClipboard();
        }

        /// <summary>
        /// Display a window to edit multiple creatures at once. Also used to set tags.
        /// </summary>
        private void ShowMultiSetter()
        {
            // shows a dialog to set multiple settings to all selected creatures
            if (listViewLibrary.SelectedIndices.Count <= 0)
                return;
            Creature c = new Creature();
            List<Creature> selectedCreatures = new List<Creature>();

            // check if multiple species are selected
            bool multipleSpecies = false;
            Species sp = ((Creature)listViewLibrary.SelectedItems[0].Tag).Species;
            c.Species = sp;
            foreach (ListViewItem i in listViewLibrary.SelectedItems)
            {
                selectedCreatures.Add((Creature)i.Tag);
                if (!multipleSpecies && ((Creature)i.Tag).speciesBlueprint != sp.blueprintPath)
                {
                    multipleSpecies = true;
                }
            }
            List<Creature>[] parents = null;
            if (!multipleSpecies)
                parents = FindPossibleParents(c);

            using (MultiSetter ms = new MultiSetter(selectedCreatures,
                parents,
                _creatureCollection.tags,
                Values.V.species,
                _creatureCollection.ownerList,
                _creatureCollection.tribes.Select(t => t.TribeName).ToArray(),
                _creatureCollection.serverList))
            {
                if (ms.ShowDialog() == DialogResult.OK)
                {
                    if (ms.ParentsChanged)
                        UpdateParents(selectedCreatures);
                    if (ms.TagsChanged)
                        CreateCreatureTagList();
                    if (ms.SpeciesChanged)
                        UpdateSpeciesLists(_creatureCollection.creatures);
                    UpdateOwnerServerTagLists();
                    SetCollectionChanged(true, !multipleSpecies ? sp : null);
                    RecalculateTopStatsIfNeeded();
                    FilterLibRecalculate();
                }
            }
        }

        private Debouncer filterLibraryDebouncer = new Debouncer();

        private void ToolStripTextBoxLibraryFilter_TextChanged(object sender, EventArgs e)
        {
            filterLibraryDebouncer.Debounce(ToolStripTextBoxLibraryFilter.Text == string.Empty ? 0 : 300, FilterLib, Dispatcher.CurrentDispatcher);
        }

        private void ToolStripButtonLibraryFilterClear_Click(object sender, EventArgs e)
        {
            ToolStripTextBoxLibraryFilter.Clear();
            ToolStripTextBoxLibraryFilter.Focus();
        }

        /// <summary>
        /// User can select a folder where infoGraphics for all selected creatures are saved.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void saveInfographicsToFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var si = listViewLibrary.SelectedItems;
            if (si.Count == 0) return;

            var initialFolder = Properties.Settings.Default.InfoGraphicExportFolder;
            if (string.IsNullOrEmpty(initialFolder) || !Directory.Exists(initialFolder))
                initialFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            string folderPath = null;
            using (var fs = new FolderBrowserDialog
            {
                SelectedPath = initialFolder
            })
            {
                if (fs.ShowDialog() == DialogResult.OK)
                    folderPath = fs.SelectedPath;
            }

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

            Properties.Settings.Default.InfoGraphicExportFolder = folderPath;

            // test if files can be written to the folder
            var testFileName = "testFile.txt";
            try
            {
                var testFilePath = Path.Combine(folderPath, testFileName);
                File.WriteAllText(testFilePath, string.Empty);
                FileService.TryDeleteFile(testFilePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBoxes.ExceptionMessageBox(ex, $"The selected folder\n{folderPath}\nis protected, the files cannot be saved there. Select a different folder.");
                return;
            }

            int imagesCreated = 0;
            string firstImageFilePath = null;

            foreach (ListViewItem li in si)
            {
                var c = (Creature)li.Tag;
                var filePath = Path.Combine(folderPath, $"ARK_info_{c.Species.name}_{c.name}.png");
                if (File.Exists(filePath))
                {
                    switch (MessageBox.Show($"The file\n{filePath}\nalready exists.\nOverwrite the file?", "File exists already", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                    {
                        case DialogResult.No: continue;
                        case DialogResult.Yes: break;
                        default: return;
                    }
                }
                c.InfoGraphic(_creatureCollection).Save(filePath);
                if (firstImageFilePath == null) firstImageFilePath = filePath;

                imagesCreated++;
            }

            if (imagesCreated == 0) return;

            var pluralS = (imagesCreated != 1 ? "s" : string.Empty);
            SetMessageLabelText($"Infographic{pluralS} for {imagesCreated} creature{pluralS} created at\n{(imagesCreated == 1 ? firstImageFilePath : folderPath)}", MessageBoxIcon.Information, firstImageFilePath);
        }

        /// <summary>
        /// Selects a creature in the library
        /// </summary>
        /// <param name="creature"></param>
        private void SelectCreatureInLibrary(Creature creature)
        {
            if (creature == null) return;

            ListViewItem lviCreature = null;
            foreach (ListViewItem lvi in listViewLibrary.Items)
            {
                if (lvi.Tag is Creature c && c == creature)
                {
                    lviCreature = lvi;
                    break;
                }
            }

            if (lviCreature == null) return;

            _reactOnCreatureSelectionChange = false;
            // deselect
            foreach (ListViewItem lvi in listViewLibrary.SelectedItems)
                lvi.Selected = false;
            _reactOnCreatureSelectionChange = true;

            lviCreature.Focused = true;
            lviCreature.Selected = true;
            listViewLibrary.EnsureVisible(lviCreature.Index);
        }
    }
}
