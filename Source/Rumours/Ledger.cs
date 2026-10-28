using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Rumours
{
    /// <summary>One person knowing something private about another.</summary>
    public class Secret : IExposable
    {
        public Pawn holder;
        public Pawn subject;
        public int toldTick;

        public Secret() { }

        public Secret(Pawn holder, Pawn subject)
        {
            this.holder = holder;
            this.subject = subject;
            toldTick = Find.TickManager.TicksGame;
        }

        public bool Valid => holder != null && subject != null && !holder.Dead && !subject.Dead;

        public void ExposeData()
        {
            Scribe_References.Look(ref holder, "holder");
            Scribe_References.Look(ref subject, "subject");
            Scribe_Values.Look(ref toldTick, "toldTick", 0);
        }
    }

    /// <summary>Who is holding whose business, and who repeats it.</summary>
    public class GameComponent_Rumours : GameComponent
    {
        private const int TickInterval = 1000;

        private List<Secret> secrets = new List<Secret>();
        private static GameComponent_Rumours instance;

        public GameComponent_Rumours(Game game)
        {
            instance = this;
        }

        public static GameComponent_Rumours Instance => instance ?? Current.Game?.GetComponent<GameComponent_Rumours>();

        public override void FinalizeInit()
        {
            instance = this;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref secrets, "secrets", LookMode.Deep);
            if (secrets == null)
            {
                secrets = new List<Secret>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                secrets.RemoveAll(s => s == null || s.holder == null || s.subject == null);
            }
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % TickInterval != 0)
            {
                return;
            }
            for (int i = secrets.Count - 1; i >= 0; i--)
            {
                if (secrets[i] == null || !secrets[i].Valid)
                {
                    secrets.RemoveAt(i);
                }
            }
        }

        // ---- secrets ------------------------------------------------------

        public void RecordSecret(Pawn holder, Pawn subject)
        {
            if (holder == null || subject == null || holder == subject)
            {
                return;
            }
            for (int i = 0; i < secrets.Count; i++)
            {
                if (secrets[i].holder == holder && secrets[i].subject == subject)
                {
                    return;
                }
            }
            secrets.Add(new Secret(holder, subject));
        }

        public bool Holds(Pawn holder, Pawn subject)
        {
            for (int i = 0; i < secrets.Count; i++)
            {
                if (secrets[i].holder == holder && secrets[i].subject == subject && secrets[i].Valid)
                {
                    return true;
                }
            }
            return false;
        }

        public int SecretsHeldBy(Pawn holder)
        {
            int n = 0;
            for (int i = 0; i < secrets.Count; i++)
            {
                if (secrets[i].holder == holder && secrets[i].Valid) { n++; }
            }
            return n;
        }

        /// <summary>
        /// A gossip repeating something that was told to them in confidence. The person who
        /// confided finds out, because these things always get back, and does not forget it.
        /// The discreet do not do this, and feel wretched on the rare occasion they slip.
        /// </summary>
        public bool TryBetraySecret(Pawn teller, Pawn listener)
        {
            if (!RM.IsGossip(teller) || Rand.Value > Tuning.BetrayalChance)
            {
                return false;
            }
            for (int i = 0; i < secrets.Count; i++)
            {
                Secret s = secrets[i];
                if (s == null || !s.Valid || s.holder != teller || s.subject == listener)
                {
                    continue;
                }
                // The listener now thinks less of the person whose business it was.
                Thought_Rumour told = (Thought_Rumour)ThoughtMaker.MakeThought(RM_DefOf.RM_HeardAbout);
                told.belief = RM.Belief(teller, listener);
                told.invented = false;
                listener.needs?.mood?.thoughts?.memories?.TryGainMemory(told, s.subject);

                // And the person who trusted them finds out.
                s.subject.needs?.mood?.thoughts?.memories?.TryGainMemory(RM_DefOf.RM_Betrayed, teller);
                if (RM.IsDiscreet(teller))
                {
                    teller.needs?.mood?.thoughts?.memories?.TryGainMemory(RM_DefOf.RM_ShouldNotHaveSaid);
                }
                if (PawnUtility.ShouldSendNotificationAbout(s.subject))
                {
                    Messages.Message(
                        $"{s.subject.LabelShortCap} found out that {teller.LabelShort} had been repeating their business.",
                        s.subject, MessageTypeDefOf.NegativeEvent);
                }
                secrets.RemoveAt(i);
                return true;
            }
            return false;
        }

        // ---- rumours ------------------------------------------------------

        /// <summary>
        /// Who the story is about. A gossip repeats what they actually think of someone; a
        /// fabricator picks a target and invents. Nobody gossips about the person in front of them.
        /// </summary>
        public Pawn PickSubject(Pawn teller, Pawn listener, out bool invented, out bool kind)
        {
            invented = false;
            kind = false;
            if (teller?.Map == null)
            {
                return null;
            }
            List<Pawn> candidates = new List<Pawn>();
            IReadOnlyList<Pawn> all = teller.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p != teller && p != listener && RM.CanTalk(p))
                {
                    candidates.Add(p);
                }
            }
            if (candidates.Count == 0)
            {
                return null;
            }
            Pawn subject = candidates.RandomElement();

            if (RM.IsFabricator(teller) && Tuning.Fabrication)
            {
                // Invented outright, and rarely kind. None of it needs to be true.
                invented = true;
                kind = false;
                return subject;
            }
            // Otherwise they pass on what they genuinely think, which is sometimes generous.
            int opinion = RM.Opinion(teller, subject);
            kind = opinion > 20;
            if (opinion > -10 && opinion <= 20)
            {
                return null;    // nothing much to say about them either way
            }
            return subject;
        }

        // ---- apologies ----------------------------------------------------

        /// <summary>
        /// The memories an apology can take the edge off. A betrayed secret is deliberately not
        /// on this list: being sorry does not unsay it.
        /// </summary>
        private static readonly string[] Forgivable =
        {
            "Insulted", "Slighted", "HarmedMe", "HadAngeringFight", "RM_HeardAbout"
        };

        public static bool HasSomethingToApologiseFor(Pawn wronged, Pawn wrongdoer)
        {
            List<Thought_Memory> memories = wronged?.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null || wrongdoer == null)
            {
                return false;
            }
            for (int i = 0; i < memories.Count; i++)
            {
                Thought_Memory m = memories[i];
                if (m?.def == null || m.otherPawn != wrongdoer)
                {
                    continue;
                }
                for (int f = 0; f < Forgivable.Length; f++)
                {
                    if (m.def.defName == Forgivable[f])
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Clears some of what was done. Not all of it, and never the betrayal.</summary>
        public static void ForgiveSome(Pawn wronged, Pawn wrongdoer)
        {
            MemoryThoughtHandler memories = wronged?.needs?.mood?.thoughts?.memories;
            if (memories == null || wrongdoer == null)
            {
                return;
            }
            for (int f = 0; f < Forgivable.Length; f++)
            {
                ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(Forgivable[f]);
                if (def != null)
                {
                    memories.RemoveMemoriesOfDefWhereOtherPawnIs(def, wrongdoer);
                }
            }
        }
    }
}
