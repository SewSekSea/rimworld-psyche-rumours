using UnityEngine;
using Verse;

namespace Rumours
{
    public class RumoursSettings : ModSettings
    {
        public float secretWeight = 0.25f;
        public float rumourWeight = 0.4f;
        public float apologyWeight = 0.5f;
        public float betrayalChance = 0.35f;

        public bool fabrication = true;
        public bool apologies = true;
        public bool crewBelief = true;

        public override void ExposeData()
        {
            RumoursSettings d = new RumoursSettings();
            Scribe_Values.Look(ref secretWeight, "secretWeight", d.secretWeight);
            Scribe_Values.Look(ref rumourWeight, "rumourWeight", d.rumourWeight);
            Scribe_Values.Look(ref apologyWeight, "apologyWeight", d.apologyWeight);
            Scribe_Values.Look(ref betrayalChance, "betrayalChance", d.betrayalChance);
            Scribe_Values.Look(ref fabrication, "fabrication", true);
            Scribe_Values.Look(ref apologies, "apologies", true);
            Scribe_Values.Look(ref crewBelief, "crewBelief", true);
        }

        public void Reset()
        {
            RumoursSettings d = new RumoursSettings();
            secretWeight = d.secretWeight;
            rumourWeight = d.rumourWeight;
            apologyWeight = d.apologyWeight;
            betrayalChance = d.betrayalChance;
            fabrication = apologies = crewBelief = true;
        }
    }

    public class RumoursMod : Mod
    {
        public static RumoursSettings Settings = new RumoursSettings();

        public RumoursMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RumoursSettings>();
        }

        public override string SettingsCategory() => "Rumours";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            RumoursSettings s = Settings;
            Listing_Standard list = new Listing_Standard { ColumnWidth = (inRect.width - 34f) / 2f };
            list.Begin(inRect);

            list.Label("How much people talk");
            s.secretWeight = list.SliderLabeled($"Sharing a secret, frequency {s.secretWeight:0.00}",
                s.secretWeight, 0f, 1f, 0.6f,
                "How often a colonist tells someone they trust something private. Vanilla chit-chat is 1.00.");
            s.rumourWeight = list.SliderLabeled($"Passing on a rumour, frequency {s.rumourWeight:0.00}",
                s.rumourWeight, 0f, 1.5f, 0.6f,
                "How often gossips repeat things, and how hard what they say lands. Set to 0 to stop rumours entirely.");
            s.apologyWeight = list.SliderLabeled($"Apologising, frequency {s.apologyWeight:0.00}",
                s.apologyWeight, 0f, 1.5f, 0.6f);
            s.betrayalChance = list.SliderLabeled($"A gossip repeats a secret {s.betrayalChance:P0} of the time",
                s.betrayalChance, 0f, 1f, 0.6f,
                "Each time a gossip is passing something on, the chance it is a secret someone told them in confidence rather than ordinary talk.");

            list.NewColumn();
            list.Label("Optional behaviours");
            list.CheckboxLabeled("Fabrication", ref s.fabrication,
                "Colonists with the fabricator trait invent things outright. Switch this off and they only repeat what they actually believe, like an ordinary gossip.");
            list.CheckboxLabeled("Apologies", ref s.apologies,
                "Colonists can apologise for what they did, which clears some of it. It never clears a betrayed secret.");
            list.CheckboxLabeled("Crew lines", ref s.crewBelief,
                "With the Crew mod installed, a rumour from your own crew is believed far more readily than the same words from a rival.");

            list.Gap(12f);
            list.Label("A betrayed secret is not forgivable by apology and is remembered for ninety days. That is deliberate.");

            list.Gap(24f);
            if (list.ButtonText("Reset to defaults"))
            {
                s.Reset();
            }
            list.End();
        }
    }
}
