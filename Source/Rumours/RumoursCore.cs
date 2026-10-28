using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Rumours
{
    [DefOf]
    public static class RM_DefOf
    {
        public static TraitDef RM_Tongue;
        public static ThoughtDef RM_TrustedMe;
        public static ThoughtDef RM_Betrayed;
        public static ThoughtDef RM_HeardAbout;
        public static ThoughtDef RM_HeardWell;
        public static ThoughtDef RM_ShouldNotHaveSaid;
        public static ThoughtDef RM_Apologised;
        public static ThoughtDef RM_HadToApologise;
        public static InteractionDef RM_ShareSecret;
        public static InteractionDef RM_Rumour;
        public static InteractionDef RM_Apologise;

        static RM_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RM_DefOf));
        }
    }

    /// <summary>All the tuning numbers in one place. The ones players can change come from the settings menu.</summary>
    public static class Tuning
    {
        private static RumoursSettings S => RumoursMod.Settings;

        public static float SecretWeight => S.secretWeight;
        public static float RumourWeight => S.rumourWeight;
        public static float ApologyWeight => S.apologyWeight;

        /// <summary>Chance a gossip repeats a secret they are holding, each time they could.</summary>
        public static float BetrayalChance => S.betrayalChance;

        public static bool Fabrication => S.fabrication;
        public static bool Apologies => S.apologies;
        public static bool CrewBelief => S.crewBelief;

        public const int DegreeDiscreet = -1;
        public const int DegreeGossip = 1;
        public const int DegreeFabricator = 2;

        /// <summary>Bounds on how hard a rumour lands, before the thought's own numbers.</summary>
        public const float MinBelief = 0.25f;
        public const float MaxBelief = 1.8f;

        /// <summary>A rumour from someone in your own crew carries this much extra weight.</summary>
        public const float CrewBeliefBonus = 0.5f;

        /// <summary>And from a rival, this much less.</summary>
        public const float RivalBeliefPenalty = 0.4f;
    }

    [StaticConstructorOnStartup]
    public static class RM
    {
        static RM()
        {
            new Harmony("rumours.gossip").PatchAll();
            Log.Message($"[Rumours] Loaded.{(CrewBridge.Available ? " Crew found: rumours travel along crew lines." : string.Empty)}");
        }

        public static int Tongue(Pawn p)
        {
            Trait t = p?.story?.traits?.GetTrait(RM_DefOf.RM_Tongue);
            return t == null ? 0 : t.Degree;
        }

        public static bool IsDiscreet(Pawn p) => Tongue(p) == Tuning.DegreeDiscreet;
        public static bool IsGossip(Pawn p) => Tongue(p) >= Tuning.DegreeGossip;
        public static bool IsFabricator(Pawn p) => Tongue(p) >= Tuning.DegreeFabricator;

        public static bool CanTalk(Pawn p)
        {
            return p != null && !p.Dead && p.RaceProps != null && p.RaceProps.Humanlike &&
                   p.needs?.mood != null && p.IsColonist && !p.DevelopmentalStage.Juvenile();
        }

        public static int Opinion(Pawn of, Pawn about)
        {
            if (of?.relations == null || about == null || of == about)
            {
                return 0;
            }
            return of.relations.OpinionOf(about);
        }

        /// <summary>
        /// How much a listener takes a story to heart. Driven by what they think of the teller,
        /// how persuasive the teller is, and, if the Crew mod is running, whose side they are on.
        ///
        /// People believe their own. That is most of how this sort of thing actually spreads.
        /// </summary>
        public static float Belief(Pawn teller, Pawn listener)
        {
            float b = 0.6f;

            int opinion = Opinion(listener, teller);
            b += Math.Max(-0.25f, Math.Min(0.5f, opinion * 0.006f));

            SkillRecord social = teller?.skills?.GetSkill(SkillDefOf.Social);
            if (social != null && !social.TotallyDisabled)
            {
                b += social.Level * 0.03f;
            }
            if (IsFabricator(teller))
            {
                b += 0.2f;      // they are simply better at being believed than they deserve
            }
            if (IsDiscreet(listener))
            {
                b -= 0.2f;      // slower to take someone else's word for it
            }

            if (Tuning.CrewBelief)
            {
                if (CrewBridge.SameCrew(teller, listener)) { b += Tuning.CrewBeliefBonus; }
                else if (CrewBridge.AreRivals(teller, listener)) { b -= Tuning.RivalBeliefPenalty; }
            }

            if (b < Tuning.MinBelief) { return Tuning.MinBelief; }
            return b > Tuning.MaxBelief ? Tuning.MaxBelief : b;
        }

        public static bool HasMemoryAbout(Pawn pawn, ThoughtDef def, Pawn about)
        {
            if (def == null || about == null)
            {
                return false;
            }
            List<Thought_Memory> memories = pawn?.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null)
            {
                return false;
            }
            for (int i = 0; i < memories.Count; i++)
            {
                if (memories[i].def == def && memories[i].otherPawn == about)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// A one-way soft link to the Crew mod. People believe their own crew and discount their
    /// rivals, which is most of how a rumour actually travels. Found by name at runtime, so
    /// Rumours never requires Crew and cannot break if Crew is absent or changes.
    /// </summary>
    public static class CrewBridge
    {
        private static bool looked;
        private static MethodInfo sameCrew;
        private static MethodInfo areRivals;

        public static bool Available
        {
            get
            {
                Look();
                return sameCrew != null && areRivals != null;
            }
        }

        private static void Look()
        {
            if (looked)
            {
                return;
            }
            looked = true;
            try
            {
                Type t = AccessTools.TypeByName("Crew.GameComponent_Crews");
                if (t != null)
                {
                    sameCrew = AccessTools.Method(t, "SameCrew", new[] { typeof(Pawn), typeof(Pawn) });
                    areRivals = AccessTools.Method(t, "AreRivals", new[] { typeof(Pawn), typeof(Pawn) });
                }
            }
            catch
            {
                sameCrew = null;
                areRivals = null;
            }
        }

        public static bool SameCrew(Pawn a, Pawn b)
        {
            return Invoke(sameCrew, a, b);
        }

        public static bool AreRivals(Pawn a, Pawn b)
        {
            return Invoke(areRivals, a, b);
        }

        private static bool Invoke(MethodInfo m, Pawn a, Pawn b)
        {
            Look();
            if (m == null || a == null || b == null)
            {
                return false;
            }
            try
            {
                return (bool)m.Invoke(null, new object[] { a, b });
            }
            catch
            {
                // Another mod's internals are not this mod's business to guarantee.
                return false;
            }
        }
    }
}
