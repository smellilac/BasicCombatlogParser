﻿using CombatlogParser.Data;
using CombatlogParser.Data.Events;
using CombatlogParser.Data.Metadata;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CombatlogParser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //private Combatlog currentCombatlog = new();
        private ObservableCollection<CombatlogEvent> events = new();
        private ObservableCollection<DamageEvent> damageEvents = new();

        //TODO: assign pets to owners, process damage accordingly
        private Dictionary<string, string> petToOwnerGUID = new();

        private Dictionary<string, DamageSummary> damageSumDict = new();
        private ObservableCollection<DamageSummary> damageSummaries = new();

        public static EncounterInfo[] ParsedEncounters { get; internal set; } = Array.Empty<EncounterInfo>();
        public static EncounterInfoMetadata[] Encounters { get; internal set; }

        public MainWindow()
        {
            InitializeComponent();

            //apply the binding to the Label.ContentProperty 
            HeaderLabel.Content = "Combatlogs";

            ////try reading a large combatlog into a full Combatlog object.
            //currentCombatlog = CombatLogParser.ReadCombatlogFile("combatlogLarge.txt");

            for (int i = 0; i < ParsedEncounters.Length; i++)
            {
                var enc = ParsedEncounters[i];
                EncounterSelection.Items.Add($"{i}:{enc.EncounterName}: {(enc.EncounterSuccess ? "Kill" : "Wipe")}  - ({ParsingUtil.MillisecondsToReadableTimeString(enc.EncounterDuration)})");
            }
            EncounterSelection.SelectionChanged += OnEncounterChanged;

            var dmgEventsBinding = new Binding()
            {
                Source = damageEvents
            };
            DamageEventsList.SetBinding(ListView.ItemsSourceProperty, dmgEventsBinding);

            //var eventsBinding = new Binding()
            //{
            //    Source = events
            //};
            //CombatLogEventsList.SetBinding(ListView.ItemsSourceProperty, eventsBinding); //the "All Events" list is disabled.

            var dmgBreakdownBinding = new Binding()
            {
                Source = damageSummaries
            };
            DmgPerSourceList.SetBinding(ListView.ItemsSourceProperty, dmgBreakdownBinding);
            //This bit works for initializing the Content to "Test", but it will not receive updates, as
            //the class does not implement INotifyPropertyChanged
            //var binding = new Binding("Message")
            //{
            //Source = test
            //};
            //HeaderLabel.SetBinding(Label.ContentProperty, binding);
        }

        private void OnEncounterChanged(object sender, SelectionChangedEventArgs e)
        {
            int index = EncounterSelection.SelectedIndex;
            EncounterInfo encounter = ParsedEncounters[index];
            damageEvents.Clear();

            IEventFilter filter = new AllOfFilter(
                    new TargetFlagFilter(UnitFlag.COMBATLOG_OBJECT_REACTION_HOSTILE | UnitFlag.COMBATLOG_OBJECT_TYPE_NPC), //to hostile NPCs
                    new AnyOfFilter(
                        new SourceFlagFilter(UnitFlag.COMBATLOG_OBJECT_REACTION_FRIENDLY), //by either friendly
                        new SourceFlagFilter(UnitFlag.COMBATLOG_OBJECT_REACTION_NEUTRAL) //or neutral sources
                    )
                );

            var allDamageEvents = encounter.CombatlogEvents.GetEvents<DamageEvent>();
            var filteredDamageEvents = allDamageEvents.AllThatMatch(filter); //basically all damage to enemies.

            foreach (var d in allDamageEvents)
                damageEvents.Add(d);

            damageSumDict.Clear();
            damageSummaries.Clear();
            petToOwnerGUID.Clear();

            //1. Populate the damageSumDict with all unique players.
            foreach(var player in encounter.Players)
            {
                damageSumDict.Add(player.GUID, new() {
                    SourceName = encounter.CombatlogEvents.First(x => x.SourceGUID == player.GUID)?.SourceName ?? "Unknown"
                });
                
            }
            
            //2. register all pets that were summoned during the encounter.
            foreach(SummonEvent summonEvent in encounter.CombatlogEvents.GetEvents<SummonEvent>())
            {
                //pets are the target, source is the summoning player.
                //no check needed here, dictionary is guaranteed to be empty. 
                //summon events only happen once per unit summoned
                petToOwnerGUID.Add(summonEvent.TargetGUID, summonEvent.SourceGUID); 
            }
            //3. accessing advanced params, therefore need to check if advanced logging is enabled.
            if (false) //--NOTE THIS NEEDS AN UPDATE.
            {
                //register all pets that had some form of cast_success
                foreach (CombatlogEvent castEvent in encounter.AllEventsThatMatch(
                    SubeventFilter.CastSuccessEvents, //SPELL_CAST_SUCCESS
                    new NotFilter(new SourceFlagFilter(UnitFlag.COMBATLOG_OBJECT_TYPE_PLAYER)), //Guardians / pets / NPCs 
                    new NotFilter(new SourceFlagFilter(UnitFlag.COMBATLOG_OBJECT_REACTION_HOSTILE)) //allied guardians/pets are never hostile.
                ))
                {
                    if (petToOwnerGUID.ContainsKey(castEvent.SourceGUID) == false) //dont add duplicates of course.
                        petToOwnerGUID.Add(castEvent.SourceGUID, castEvent.GetOwnerGUID());
                }
            }

            //4. sort out the damage events.
            foreach(var dmgevent in filteredDamageEvents)
            {
                string sourceGUID = dmgevent.SourceGUID;
                //check for pet melee attacks
                //check if this is registered as a pet. warlock demons do not have the pet flag, therefore cant be easily checked outside of SPELL_SUMMON events.
                if (petToOwnerGUID.TryGetValue(sourceGUID, out string? ownerGUID))
                {
                    sourceGUID = ownerGUID;
                }
                //else if (dmgevent.IsSourcePet && dmgevent.SubeventPrefix == CombatlogEventPrefix.SWING && false) //--WOAH
                //{
                //    //pet swing damage as the owner GUID as advanced param
                //    sourceGUID = dmgevent.GetOwnerGUID();
                //    if (petToOwnerGUID.ContainsKey(dmgevent.SourceGUID) == false)
                //        petToOwnerGUID[dmgevent.SourceGUID] = sourceGUID;
                //}

                //add to existing data
                if (damageSumDict.TryGetValue(sourceGUID, out DamageSummary? sum))
                {
                    sum.TotalDamage += dmgevent.amount;
                }
                else //create new sum
                {
                    damageSumDict[sourceGUID] = new()
                    {
                        SourceName = dmgevent.SourceName, //--NOTE: This sometimes still adds a summary with the Pets name. Which is bad.
                        TotalDamage = dmgevent.amount
                    };
                }
            }

            /* skipping this bit for now.
            //add up all damage done to absorb shields on enemies.
            foreach(CombatlogEvent absorbEvent in currentCombatlog.Encounters[index].AllEventsThatMatch(
                MissTypeFilter.Absorbed,
                new TargetFlagFilter(UnitFlag.COMBATLOG_OBJECT_REACTION_HOSTILE | UnitFlag.COMBATLOG_OBJECT_TYPE_NPC),
                new SourceFlagFilter(UnitFlag.COMBATLOG_OBJECT_AFFILIATION_RAID)
                ))
            {
                if (petToOwnerGUID.TryGetValue(absorbEvent.SourceGUID, out string? sourceGUID) == false)
                    sourceGUID = absorbEvent.SourceGUID;

                if (damageSumDict.TryGetValue(sourceGUID, out DamageSummary? sum))
                    sum.TotalDamage += uint.Parse((string)absorbEvent.SuffixParam2);
                else
                    damageSumDict[sourceGUID] = new()
                    {
                        SourceName = absorbEvent.SourceName,
                        TotalDamage = uint.Parse((string)absorbEvent.SuffixParam2)
                    };
            }*/


            List<DamageSummary> sums = new List<DamageSummary>(damageSumDict.Values);
            sums.Sort((x, y) => x.TotalDamage > y.TotalDamage ? -1 : 1);
            //divide damage to calculate DPS across the encounter.
            float encounterSeconds = ParsedEncounters[index].LengthInSeconds;
            foreach(var dmgsum in sums)
            {
                dmgsum.DPS = dmgsum.TotalDamage / encounterSeconds;
                damageSummaries.Add(dmgsum);
            }
        }
    }
}
