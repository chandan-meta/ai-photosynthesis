using System;
using System.Collections.Generic;

namespace Photosynthesis
{
    /// Storyboard question bank (Q1–Q20) + adaptive gating and final selection.
    /// Score starts at 0 (Average): correct first try +1 (predictions +0.5), wrong −1 (wrong predictions 0),
    /// correct after asking Dr. Curio with the card open 0 (no credit, no penalty).
    /// Level: Struggling ≤ −1.5 · Average · High ≥ +2. Spoken lines live in curio_lines.txt as q_<ID>_lead/_ok/_no.
    public class Assessment
    {
        public Level Level => score <= -1.5f ? Level.Struggling : score >= 2f ? Level.High : Level.Average;
        public int Asked { get; private set; }
        public int CorrectFirstTry { get; private set; }
        /// Complete-card score: predictions/discoveries are left out (Curio frames them as safe guesses).
        public int Scored { get; private set; }
        public int ScoredCorrect { get; private set; }

        float score;
        readonly Dictionary<string, bool> results = new Dictionary<string, bool>();   // id → last first-try result
        readonly Dictionary<Concept, bool> mastery = new Dictionary<Concept, bool>();
        readonly List<string> missed = new List<string>();                          // ids answered wrong (not yet re-mastered), oldest first

        // Live final set. Slots are private copies so the set can adapt while Story enumerates the list.
        // ponytail: Story's foreach can't see a re-ordered list; upgrade path = Story asks NextFinal() per question.
        List<Question> slots; int slotNext; Level slotLevel; readonly List<string> rechecks = new List<string>();

        public Question Get(string id) => id != null && ById.TryGetValue(id, out var q) ? q : null;

        /// Should an in-journey question be asked to this learner right now?
        public bool ShouldAsk(string id)
        {
            if (Get(id) == null || results.ContainsKey(id)) return false;   // never the same id twice in the journey
            var lv = Level;
            switch (id)
            {
                case "Q2": return results.TryGetValue("Q1", out var q1) && q1;
                case "Q4": case "Q7": case "Q8": case "Q11": return lv != Level.High;
                case "Q6": case "Q10": return lv != Level.Struggling;
                case "Q12": return lv == Level.High || (lv == Level.Average && CorrectFirstTry >= 3);
                default: return true;   // Q1, Q3, Q5, Q9, Q13 always
            }
        }

        /// `helped`: the learner asked Dr. Curio while the card was open. A correct-but-helped answer earns no credit
        /// (no score, no CorrectFirstTry, no gating credit for Q2/Q12) but is neither penalised nor queued for a re-check.
        public void Record(Question q, bool correctFirstTry, bool helped = false)
        {
            if (q == null) return;
            bool credit = correctFirstTry && !helped;
            Asked++; if (credit) CorrectFirstTry++;
            if (!q.prediction) { Scored++; if (credit) ScoredCorrect++; }
            score += credit ? (q.prediction ? 0.5f : 1f) : (correctFirstTry || q.prediction ? 0f : -1f);
            results[q.id] = credit;
            if (credit)
            {
                mastery[q.concept] = true;
                missed.RemoveAll(m => Family(Get(m).concept) == Family(q.concept));
            }
            else if (!correctFirstTry && !q.prediction)
            {   // a wrong prediction isn't evidence of a gap — the journey hadn't taught it yet
                mastery[q.concept] = false;
                if (!missed.Contains(q.id)) missed.Add(q.id);
            }
            if (slots != null && slotNext < slots.Count && slots[slotNext].id == q.id)
            {
                slotNext++;
                if (Level != slotLevel) Replan();
            }
        }

        /// Final adaptive set (about 5) chosen from the pool based on the journey so far, ordered easy → hard:
        /// up to 2 re-checks of concepts answered wrong earlier, then the level's core questions.
        public List<Question> FinalSet(int count = 5)
        {
            count = Math.Max(1, count);
            var lv = Level; var core = Core(lv);
            rechecks.Clear();
            foreach (var id in missed)
            {
                if (rechecks.Count == 2) break;
                var fam = Family(Get(id).concept);
                if (rechecks.Exists(r => Family(Get(r).concept) == fam)) continue;
                // prefer a final question already in the plan, then the easiest matching final, then the original
                string pick = core.Find(f => Family(Get(f).concept) == fam);
                if (pick == null) foreach (var f in Finals) if (Family(Get(f).concept) == fam && (pick == null || Difficulty(f) < Difficulty(pick))) pick = f;
                rechecks.Add(pick ?? id);
            }
            var ids = new List<string>(rechecks);
            foreach (var id in core) if (ids.Count < count && !ids.Contains(id)) ids.Add(id);
            ids.Sort(ByDifficulty);
            slots = new List<Question>(ids.Count);
            foreach (var id in ids) slots.Add(Copy(Get(id), new Question()));
            slotNext = 0; slotLevel = lv;
            return slots;
        }

        /// The learner's level changed mid-set: keep pending re-checks, re-pick the rest for the new level.
        void Replan()
        {
            slotLevel = Level;
            int remaining = slots.Count - slotNext;
            var used = new List<string>();
            for (int i = 0; i < slotNext; i++) used.Add(slots[i].id);
            var next = new List<string>();
            for (int i = slotNext; i < slots.Count; i++) if (rechecks.Contains(slots[i].id)) next.Add(slots[i].id);
            foreach (var id in Core(slotLevel)) if (next.Count < remaining && !used.Contains(id) && !next.Contains(id)) next.Add(id);
            // top up from the remaining finals nearest the learner's level (easiest first when struggling, hardest when high)
            var spare = new List<string>(Finals);
            spare.Sort(ByDifficulty); if (slotLevel == Level.High) spare.Reverse();
            foreach (var id in spare) if (next.Count < remaining && !used.Contains(id) && !next.Contains(id)) next.Add(id);
            next.Sort(ByDifficulty);
            for (int i = 0; i < next.Count && slotNext + i < slots.Count; i++) Copy(Get(next[i]), slots[slotNext + i]);
        }

        public IReadOnlyDictionary<Concept, bool> Mastery => mastery;

        public void Reset()
        {
            Asked = 0; CorrectFirstTry = 0; Scored = 0; ScoredCorrect = 0; score = 0;
            results.Clear(); mastery.Clear(); missed.Clear(); rechecks.Clear();
            slots = null; slotNext = 0;
        }

        // ------------------------------------------------------------------ selection helpers
        static readonly string[] Finals = { "Q14", "Q15", "Q16", "Q17", "Q18", "Q19", "Q20" };

        List<string> Core(Level lv) => lv switch
        {
            Level.Struggling => new List<string> { "Q19", "Q14", "Q16" },
            Level.High => new List<string> { "Q15", "Q17", "Q18", "Q20", "Q16" },
            _ => new List<string> { "Q14", "Q15", "Q16", "Q19", score > 0f ? "Q20" : "Q17" }
        };

        static Concept Family(Concept c) => c switch
        {
            Concept.Xylem => Concept.Water,
            Concept.Chlorophyll => Concept.Chloroplast,
            Concept.Equation => Concept.Integration,
            _ => c
        };

        static int Difficulty(string id)
        {
            var q = ById[id];
            int rank = q.type switch
            {
                QType.Recall => 0, QType.Discovery => 1, QType.Prediction => 1, QType.Understanding => 2,
                QType.Application => 3, QType.Reasoning => 4, _ => 5
            };
            return (q.tier * 10 + rank) * 100 + int.Parse(id.Substring(1));   // ties keep bank order
        }
        static int ByDifficulty(string a, string b) => Difficulty(a).CompareTo(Difficulty(b));

        static Question Copy(Question s, Question d)
        {
            d.id = s.id; d.type = s.type; d.tier = s.tier; d.concept = s.concept; d.lead = s.lead; d.text = s.text;
            d.options = s.options; d.correct = s.correct; d.correctFeedback = s.correctFeedback; d.wrongFeedback = s.wrongFeedback;
            return d;
        }

        // ------------------------------------------------------------------ the bank (storyboard "ADAPTIVE MCQ QUESTION BANK")
        static Question Q(string id, QType type, int tier, Concept c, string lead, string text, string[] options, int correct, string ok, string no)
            => new Question { id = id, type = type, tier = tier, concept = c, lead = lead, text = text, options = options, correct = correct, correctFeedback = ok, wrongFeedback = no };

        static readonly Dictionary<string, Question> ById = Build(
            Q("Q1", QType.Recall, 1, Concept.Stomata,
                "Before we leave this leaf surface—what exactly did we just catch entering?",
                "Which gas enters the leaf through the stomata for photosynthesis?",
                new[] { "Oxygen", "Carbon dioxide", "Hydrogen", "Nitrogen" }, 1,
                "Carbon dioxide from the air enters through the stomata.",
                "Watch our visitor again."),
            Q("Q2", QType.Understanding, 2, Concept.Stomata,
                "That was easy. Try this.",
                "If the stomata close, which material needed for photosynthesis would have more difficulty entering the leaf?",
                new[] { "Water", "Minerals", "Glucose", "Carbon dioxide" }, 3,
                "Closed stomata restrict the entry of carbon dioxide.",
                "Water and minerals arrive from the roots. The stomata are the doorway for a gas from the air: carbon dioxide."),
            Q("Q3", QType.Prediction, 2, Concept.Water,
                "Signal detected below us. Any prediction?",
                "Where do you think the plant gets most of the water used in photosynthesis?",
                new[] { "Through its flowers", "Directly through the stem from the air", "From the soil through its roots", "From glucose inside the leaf" }, 2,
                "Good prediction. Let's go underground and prove it.",
                "Interesting theory. Let's investigate instead of guessing."),
            Q("Q4", QType.Recall, 1, Concept.Xylem,
                "That was quite a ride! Quick check on the tunnel we just travelled through.",
                "Which tissue carried the water from the roots toward the leaf?",
                new[] { "Phloem", "Stomata", "Xylem", "Chloroplast" }, 2,
                "Xylem is our water highway.",
                "Remember the pathway we just travelled through? That was the xylem."),
            Q("Q5", QType.Prediction, 2, Concept.Sunlight,
                "We have carbon dioxide. We have water. But food production is still zero. What's missing?",
                "What does the plant still need to drive photosynthesis?",
                new[] { "Oxygen", "Soil", "Glucose", "Sunlight" }, 3,
                "Sunlight supplies the energy that drives photosynthesis.",
                "The plant already has its raw materials. It still needs energy to drive the reaction."),
            Q("Q6", QType.Understanding, 2, Concept.Sunlight,
                "All three clues found! One tricky question about the last one.",
                "Which statement best describes the role of sunlight in photosynthesis?",
                new[] { "Sunlight becomes the plant's food directly.", "Sunlight provides energy for the process.", "Sunlight carries water into the leaves.", "Sunlight enters through the stomata as a gas." }, 1,
                "Sunlight isn't food: it's the energy that powers the food factory.",
                "Sunlight is not food and it is not a gas. Its light energy powers the reaction that turns carbon dioxide and water into food."),
            Q("Q7", QType.Discovery, 1, Concept.Chloroplast,
                "Our scanner says photosynthesis is concentrated inside these green structures. What are they?",
                "Which cell structure is the main site of photosynthesis?",
                new[] { "Nucleus", "Vacuole", "Chloroplast", "Cell wall" }, 2,
                "Target identified: chloroplast.",
                "The green, glowing structures are chloroplasts: the main site of photosynthesis."),
            Q("Q8", QType.Recall, 1, Concept.Chlorophyll,
                "Quick check on chlorophyll, the green pigment inside the chloroplasts.",
                "What is the main role of chlorophyll during photosynthesis?",
                new[] { "It absorbs light energy.", "It carries water from the roots.", "It transports food around the plant.", "It releases carbon dioxide." }, 0,
                "Chlorophyll captures the light energy needed for photosynthesis.",
                "Chlorophyll is the green pigment inside chloroplasts. It absorbs light energy. Water travels in the xylem and food in the phloem."),
            Q("Q9", QType.Prediction, 2, Concept.Glucose,
                "Unknown molecule detected. After everything we've discovered, what do you think we've made?",
                "Which food substance is produced during photosynthesis?",
                new[] { "Carbon dioxide", "Water", "Chlorophyll", "Glucose" }, 3,
                "Glucose: a sugar that stores chemical energy.",
                "Carbon dioxide and water went in. A new, energy-rich sugar comes out."),
            Q("Q10", QType.Understanding, 2, Concept.Oxygen,
                "Two journeys through the same doorway. Which way did each gas go?",
                "Which correctly describes the gas movement we observed during photosynthesis?",
                new[] { "Oxygen enters; carbon dioxide leaves.", "Both oxygen and carbon dioxide only enter.", "Carbon dioxide enters; oxygen leaves.", "Both gases only leave." }, 2,
                "Carbon dioxide in, oxygen out. Same doorway, different journey.",
                "Carbon dioxide enters through the stomata to be used. Oxygen, made in the chloroplasts, leaves through the same stomata."),
            Q("Q11", QType.Recall, 1, Concept.Phloem,
                "Now, about our second highway.",
                "Which tissue transports food from the leaves to other parts of the plant?",
                new[] { "Xylem", "Phloem", "Stomata", "Chlorophyll" }, 1,
                "Phloem carries food from the leaves to other parts of the plant.",
                "Xylem carries water up from the roots. Phloem carries food from the leaves to the rest of the plant."),
            Q("Q12", QType.Application, 3, Concept.Xylem,
                "You're on a roll. Let's make it harder.",
                "A substance is travelling upward from the roots toward a leaf. Which combination is most likely correct?",
                new[] { "Water through xylem", "Glucose through phloem", "Carbon dioxide through xylem", "Oxygen through phloem" }, 0,
                "Xylem brings water upward from the roots.",
                "Travelling up from the roots means water in the xylem. Carbon dioxide enters through the leaves, not the roots."),
            Q("Q13", QType.Understanding, 2, Concept.Equation,
                "Time to put the whole case together. What goes in, and what comes out?",
                "Which combination correctly shows the main raw materials and products of photosynthesis?",
                new[] { "Carbon dioxide + Water → Glucose + Oxygen", "Oxygen + Water → Carbon dioxide + Glucose", "Glucose + Oxygen → Carbon dioxide + Water", "Carbon dioxide + Oxygen → Water + Glucose" }, 0,
                "Case solved. Now let's see the whole process together.",
                "In: carbon dioxide from the air and water from the soil. Out: glucose and oxygen. Light energy drives it."),
            // ---- final adaptive assessment pool
            Q("Q14", QType.Application, 3, Concept.Sunlight,
                "Imagine a plant locked away in a dark cupboard.",
                "A plant has enough water and carbon dioxide but is kept in darkness. What will happen to photosynthesis?",
                new[] { "It will continue normally.", "It will increase rapidly.", "It will be severely reduced or stop because light energy is unavailable.", "It will produce more glucose." }, 2,
                "Without light energy there is nothing to drive the reaction, so photosynthesis slows to a stop.",
                "Carbon dioxide and water are only raw materials. Without light energy to drive the reaction, food production stops."),
            Q("Q15", QType.Application, 3, Concept.Stomata,
                "Here's a leaf that's running short of one ingredient.",
                "A plant receives plenty of sunlight and water, but very little carbon dioxide reaches its leaves. What is most likely to happen?",
                new[] { "Chlorophyll will turn into oxygen.", "Glucose production will increase.", "Xylem will begin transporting glucose.", "Glucose production will decrease." }, 3,
                "Carbon dioxide is a raw material for glucose. With less of it entering through the stomata, the factory makes less food.",
                "The carbon in glucose comes from carbon dioxide. If little carbon dioxide gets in through the stomata, less glucose can be made."),
            Q("Q16", QType.Reasoning, 3, Concept.Glucose,
                "Remember Manav's idea that plants get their food from the soil? Let's put it on trial.",
                "A student says, “Plants get their food from the soil.” Which observation best shows why this statement is incomplete?",
                new[] { "Plants absorb water and minerals from soil, but manufacture carbohydrates through photosynthesis.", "Leaves are above the ground.", "Soil contains small organisms.", "Roots grow underground." }, 0,
                "The soil supplies water and minerals, but the plant manufactures its own food, glucose, in its leaves by photosynthesis.",
                "Roots take in water and minerals, but the food itself, glucose, is made in the leaves by photosynthesis."),
            Q("Q17", QType.Reasoning, 3, Concept.Xylem,
                "Time for some plant detective work. Something inside this plant is damaged.",
                "A plant's xylem is damaged. Which part of photosynthesis would be affected most directly?",
                new[] { "Transport of water to the leaves", "Entry of carbon dioxide through stomata", "Transport of food from leaves", "Absorption of light by chlorophyll" }, 0,
                "Xylem carries water from the roots up to the leaves, so damage cuts off a raw material for photosynthesis.",
                "Xylem is the water highway from roots to leaves. Food travels in the phloem, and carbon dioxide enters through the stomata."),
            Q("Q18", QType.Reasoning, 3, Concept.Phloem,
                "Now the other highway is in trouble.",
                "A plant's phloem is damaged but its xylem is working normally. Which process would be most directly affected?",
                new[] { "Water reaching the leaves", "Transport of food from leaves to other parts", "Carbon dioxide entering stomata", "Chlorophyll absorbing light" }, 1,
                "Phloem carries food from the leaves to the roots, shoots and fruits. If it's damaged, the food can't travel.",
                "With healthy xylem, water still reaches the leaves. Phloem moves food out of the leaves, so that's what is affected."),
            Q("Q19", QType.Understanding, 2, Concept.Water,
                "Let's retrace the water's journey.",
                "Which sequence best follows the journey of water shown in the module?",
                new[] { "Leaf → Stem → Root → Soil", "Soil → Root → Xylem → Leaf", "Soil → Stomata → Phloem → Leaf", "Root → Phloem → Stomata → Leaf" }, 1,
                "Water is absorbed from the soil by the roots, then travels up the xylem to the leaves.",
                "Water starts in the soil, enters the roots, rides up the xylem and arrives in the leaf."),
            Q("Q20", QType.Integrated, 3, Concept.Integration,
                "Final challenge. Connect all three clues.",
                "Which statement correctly connects all three clues?",
                new[] { "Water provides energy, sunlight supplies carbon, and CO₂ transports food.", "Glucose and water are raw materials, while CO₂ provides energy.", "Sunlight and oxygen are raw materials, while water provides energy.", "CO₂ and water provide raw materials, while sunlight provides energy for photosynthesis." }, 3,
                "Carbon dioxide and water are the raw materials. Sunlight provides the energy that turns them into glucose and oxygen.",
                "Carbon dioxide and water are the raw materials. Sunlight is not an ingredient: it provides the energy.")
        );

        static Dictionary<string, Question> Build(params Question[] qs)
        {
            var d = new Dictionary<string, Question>();
            foreach (var q in qs) d[q.id] = q;
            return d;
        }
    }
}
