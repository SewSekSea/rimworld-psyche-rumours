using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Rumours
{
    /// <summary>
    /// A rumour, held by whoever believed it. How hard it lands is decided when it is heard and
    /// kept on the thought, so a story from someone persuasive keeps its weight afterwards rather
    /// than being recalculated from whatever the listener happens to think of them later.
    /// </summary>
    public class Thought_Rumour : Thought_MemorySocial
    {
        public float belief = 1f;

        /// <summary>Whether there was anything behind it. Kept for the mod's own bookkeeping.</summary>
        public bool invented;

        public override float OpinionOffset()
        {
            return base.OpinionOffset() * belief * Tuning.RumourWeight;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref belief, "belief", 1f);
            Scribe_Values.Look(ref invented, "invented", false);
        }
    }

    /// <summary>
    /// Telling someone something private. Only to people they genuinely trust, and never to
    /// someone already holding a secret of theirs that went round the colony.
    /// </summary>
    public class InteractionWorker_ShareSecret : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (!RM.CanTalk(initiator) || !RM.CanTalk(recipient))
            {
                return 0f;
            }
            // You confide in people you think well of, not in anyone who happens to be standing there.
            if (RM.Opinion(initiator, recipient) < 25)
            {
                return 0f;
            }
            // Nobody confides twice in someone who already told everyone.
            if (RM.HasMemoryAbout(initiator, RM_DefOf.RM_Betrayed, recipient))
            {
                return 0f;
            }
            float w = Tuning.SecretWeight;
            if (RM.IsDiscreet(initiator)) { w *= 1.3f; }   // they choose carefully, and do confide
            return w;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;
            GameComponent_Rumours.Instance?.RecordSecret(recipient, initiator);
        }
    }

    /// <summary>
    /// Passing on what you think of a third person. A gossip repeats what they genuinely believe;
    /// a fabricator makes it up. Either way the listener ends up thinking differently about
    /// someone they may not have spoken to in days.
    /// </summary>
    public class InteractionWorker_Rumour : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (!RM.CanTalk(initiator) || !RM.CanTalk(recipient) || !RM.IsGossip(initiator))
            {
                return 0f;
            }
            // Nobody gossips at someone they cannot stand; it is a confiding sort of act.
            if (RM.Opinion(initiator, recipient) < 0)
            {
                return 0f;
            }
            return Tuning.RumourWeight > 0f ? Tuning.RumourWeight : 0f;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;

            GameComponent_Rumours ledger = GameComponent_Rumours.Instance;
            if (ledger == null)
            {
                return;
            }

            // First: is there a secret of someone else's to repeat? That is the worst kind.
            if (ledger.TryBetraySecret(initiator, recipient))
            {
                return;
            }

            Pawn subject = ledger.PickSubject(initiator, recipient, out bool invented, out bool kind);
            if (subject == null)
            {
                return;
            }

            ThoughtDef def = kind ? RM_DefOf.RM_HeardWell : RM_DefOf.RM_HeardAbout;
            Thought_Rumour rumour = (Thought_Rumour)ThoughtMaker.MakeThought(def);
            rumour.belief = RM.Belief(initiator, recipient);
            rumour.invented = invented;
            recipient.needs?.mood?.thoughts?.memories?.TryGainMemory(rumour, subject);
        }
    }

    /// <summary>
    /// Making it right. Takes the edge off what was done, costs the one doing it something,
    /// and is not available to people who feel nothing about it in the first place.
    /// </summary>
    public class InteractionWorker_Apologise : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (!Tuning.Apologies || !RM.CanTalk(initiator) || !RM.CanTalk(recipient))
            {
                return 0f;
            }
            // Only if there is something to apologise for, and they have not just done it.
            if (!GameComponent_Rumours.HasSomethingToApologiseFor(recipient, initiator))
            {
                return 0f;
            }
            if (RM.HasMemoryAbout(recipient, RM_DefOf.RM_Apologised, initiator))
            {
                return 0f;
            }
            return Tuning.ApologyWeight;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;

            // The apology clears some of what was actually done, rather than only adding good feeling
            // on top of it. It does not clear a betrayed secret; that is not a thing sorry fixes.
            GameComponent_Rumours.ForgiveSome(recipient, initiator);
            initiator.needs?.mood?.thoughts?.memories?.TryGainMemory(RM_DefOf.RM_HadToApologise);
        }
    }
}
