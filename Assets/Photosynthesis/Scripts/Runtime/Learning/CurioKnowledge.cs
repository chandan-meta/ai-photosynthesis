using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Photosynthesis
{
    /// Dr. Curio's offline knowledge base (ages 11–15, 2–4 sentences, scientifically correct). Context-aware: answers follow
    /// the current beat, never spoil a clue before the story reveals it, and only give hints (never answers) during the final
    /// investigation or while a question card is open.
    /// Chemical formulas appear only inside (parentheses) so Spoken() can drop them for text-to-speech.
    public static class CurioKnowledge
    {
        sealed class Entry
        {
            public Beat reveal;       // before this beat → tease instead of the answer
            public string explain, why, tease, memory;
        }

        static readonly Dictionary<string, Entry> Kb = new Dictionary<string, Entry>
        {
            ["co2"] = new Entry
            {
                reveal = Beat.CO2,
                explain = "Carbon dioxide (CO₂) is a gas in the air, made of one carbon atom and two oxygen atoms. It enters the leaf through tiny pores called stomata, and it's one of the two raw materials for photosynthesis.",
                why = "Carbon dioxide supplies the carbon. Every glucose molecule the leaf builds is made from carbon taken out of the air — so without it, there's nothing to build food from.",
                tease = "Ooh, that's one of our clues — no spoilers! Here's a hint: it's invisible, and it's all around us right now.",
                memory = "the molecule we watched drifting in through the stoma"
            },
            ["stomata"] = new Entry
            {
                reveal = Beat.Stomata,
                explain = "Stomata are tiny pores, mostly on the underside of a leaf. Each one is framed by two guard cells that open and close it. Carbon dioxide comes in through them, and oxygen goes out.",
                why = "Stomata are the leaf's doorways. They let carbon dioxide in for photosynthesis and let oxygen and water vapour out — and guard cells close them when the plant needs to save water.",
                tease = "You'll meet them very soon — they're tiny doorways on the leaf. Let's zoom in and find them!",
                memory = "the tiny doorways on the underside of the leaf"
            },
            ["water"] = new Entry
            {
                reveal = Beat.Water,
                explain = "Water (H₂O) is the second raw material for photosynthesis. The roots absorb it from the soil, and the xylem carries it all the way up to the leaves.",
                why = "Water is a raw material. Inside the chloroplast, water molecules are split apart — and that's actually where the oxygen the plant releases comes from. Water also keeps the plant firm and carries minerals.",
                tease = "That's a clue we haven't cracked yet! Hint: the next signal doesn't come from the leaves at all — it comes from below.",
                memory = "the water molecule the root hair absorbed from the soil"
            },
            ["roots"] = new Entry
            {
                reveal = Beat.Roots,
                explain = "Roots anchor the plant in the soil and absorb water and minerals through thousands of tiny root hairs. That water then starts its journey upward through the xylem.",
                why = "Roots are how the plant drinks. They take in the water photosynthesis needs, along with minerals, and hold the whole plant steady.",
                tease = "Patience, investigator! Something important is hidden beneath the chamber floor. We'll dig into it soon.",
                memory = "the roots hidden beneath the chamber floor"
            },
            ["xylem"] = new Entry
            {
                reveal = Beat.XylemRide,
                explain = "Xylem is the plant's water highway: tube-like tissue that carries water and minerals upward from the roots, through the stem, to the leaves. It flows one way — up.",
                why = "Without xylem, water couldn't climb from the roots to the leaves, and the food factory would run dry.",
                tease = "You'll get to ride it yourself! For now, here's a hint: it's a hidden highway running up through the plant.",
                memory = "our wild ride up from the roots"
            },
            ["sunlight"] = new Entry
            {
                reveal = Beat.Sunlight,
                explain = "Sunlight is the energy source for photosynthesis. Chlorophyll in the leaves absorbs light energy and uses it to turn carbon dioxide and water into food. Light isn't food itself — it's the power supply.",
                why = "Light provides the energy. Without it, carbon dioxide and water just sit there — light energy powers the reaction that joins them into glucose.",
                tease = "That's our final clue, and I'm keeping it secret a little longer. Hint: every factory needs power from somewhere.",
                memory = "the moment the sunbeam hit our sleepy leaf"
            },
            ["chloroplast"] = new Entry
            {
                reveal = Beat.PlantCell,
                explain = "Chloroplasts are tiny green structures inside leaf cells — the main site of photosynthesis. Inside them, stacks called grana hold chlorophyll, which captures light energy.",
                why = "Chloroplasts are the factory floor. They hold the chlorophyll that catches light and the machinery that builds glucose from carbon dioxide and water.",
                tease = "We'll shrink down inside a leaf cell to find it. A clue: look for something green.",
                memory = "the glowing green structures inside the plant cell"
            },
            ["chlorophyll"] = new Entry
            {
                reveal = Beat.Chloroplast,
                explain = "Chlorophyll is the green pigment inside chloroplasts. It absorbs light energy — mostly red and blue light — and reflects green light, which is why leaves look green.",
                why = "Chlorophyll is the plant's light catcher. It absorbs the light energy that drives photosynthesis and passes that energy on to make food.",
                tease = "You'll see it at work very soon. Hint: it's the reason leaves are green.",
                memory = "the green pigment soaking up the light"
            },
            ["glucose"] = new Entry
            {
                reveal = Beat.Glucose,
                explain = "Glucose (C₆H₁₂O₆) is a sugar — a carbohydrate that stores chemical energy. It's the food the plant makes from carbon dioxide and water, using light energy.",
                why = "Glucose is the plant's food and fuel. It's used for energy, to build new parts like cellulose for cell walls, or turned into starch and stored for later.",
                tease = "That's the big mystery of this case! Keep investigating — we're going to catch this leaf making it.",
                memory = "the unknown molecule our scanner identified"
            },
            ["oxygen"] = new Entry
            {
                reveal = Beat.Oxygen,
                explain = "Oxygen (O₂) is the other product of photosynthesis. It comes from water molecules split inside the chloroplast, and it leaves the leaf through the same stomata that let carbon dioxide in.",
                why = "The plant only needs a little of the oxygen it makes for its own respiration, so most of it escapes through the stomata — lucky for us, because that's the oxygen we breathe!",
                tease = "Ha — that's a twist I'm saving for later. Keep your eyes on those stomata!",
                memory = "the oxygen leaving through the stoma"
            },
            ["phloem"] = new Entry
            {
                reveal = Beat.Phloem,
                explain = "Phloem is the plant's food highway. It carries sugars made in the leaves to the roots, shoots, flowers, fruits and storage organs — up or down, wherever food is needed.",
                why = "Leaves make the food, but roots, growing shoots, flowers and fruits need it too. Phloem is the delivery network that gets it there.",
                tease = "That's a second highway — we'll meet it once the leaf has made its food.",
                memory = "the orange highway carrying food away from the leaves"
            },
            ["equation"] = new Entry
            {
                reveal = Beat.Equation,
                explain = "Carbon dioxide plus water, using light energy absorbed by chlorophyll, makes glucose plus oxygen. (6CO₂ + 6H₂O → C₆H₁₂O₆ + 6O₂)",
                why = "The equation is the whole case in one line: two raw materials in, light energy to power it, food and oxygen out.",
                tease = "We'll build the whole equation together at the end — from everything you discover.",
                memory = "what went into the food factory and what came out"   // Q13 is asked before the equation is built
            },
            ["photosynthesis"] = new Entry
            {
                reveal = Beat.Sunlight,
                explain = "Photosynthesis is how plants make their own food. Using light energy, they turn carbon dioxide and water into glucose, and release oxygen. It happens mostly in the chloroplasts of leaf cells.",
                why = "Photosynthesis is how this plant feeds itself. Almost every food chain on Earth starts with it — and it puts oxygen into the air we breathe.",
                tease = "Photosynthesis is how plants make their own food. It needs three important things — and finding them is our mission!",
                memory = "everything we discovered on our journey"
            },
            ["where"] = new Entry
            {
                reveal = Beat.PlantCell,
                explain = "Mostly in the leaves — inside chloroplasts in the leaf cells. Chlorophyll there captures light, and the chloroplast turns carbon dioxide and water into glucose.",
                why = "Leaves are wide and flat to catch light, and their cells are packed with chloroplasts. That's why the food factory is there.",
                tease = "In the leaves — that much I can tell you! Exactly where inside them is a mystery we'll solve together.",
                memory = "where we found the green factory inside the cell"
            },
            ["soilfood"] = new Entry
            {
                reveal = Beat.Glucose,
                explain = "Not quite! Roots take water and minerals from the soil, but those aren't food. The plant makes its own food, glucose, in its leaves by photosynthesis.",
                why = "The soil gives the plant water and minerals, but the food itself is made in the leaves.",
                tease = "That's exactly what this investigation will answer! Hint: the soil gives the plant some things — but not everything.",
                memory = "where the food was actually made"
            },
            ["minerals"] = new Entry
            {
                reveal = Beat.Scene2Start,
                explain = "Minerals are nutrients, such as nitrogen and magnesium, that roots absorb from the soil dissolved in water. Plants need them to stay healthy — magnesium, for example, is part of every chlorophyll molecule.",
                why = "Minerals are like vitamins for the plant. They help it build proteins, chlorophyll and new cells.",
            },
            ["stem"] = new Entry
            {
                reveal = Beat.Phloem,
                explain = "The stem holds the plant up towards the light and contains its two transport highways: xylem carries water up, and phloem carries food around.",
                why = "The stem holds the leaves up to the light and links the roots to the leaves.",
                tease = "The stem holds the plant up — and it hides some secret highways we'll explore soon.",
            },
            ["cell"] = new Entry
            {
                reveal = Beat.PlantCell,
                explain = "Plant cells have a tough cell wall, a large vacuole filled with watery sap, a nucleus that controls the cell, and — in leaf cells — lots of green chloroplasts.",
                why = "Cells are the building blocks of the plant. Leaf cells are packed with chloroplasts because that's where food is made.",
                tease = "Leaves are built from millions of tiny cells. We'll shrink down and visit one soon!",
            },
            ["leaf"] = new Entry
            {
                reveal = Beat.Sunlight,
                explain = "Leaves are the plant's food factories. They're wide and flat to catch as much light as possible, with tiny stomata underneath that let gases in and out.",
                why = "Leaves are where photosynthesis mostly happens — the plant's kitchen, if you like.",
                tease = "Leaves are where the food gets made. Exactly how? That's what we're investigating!",
            },
        };

        // ------------------------------------------------------------------ public API
        /// `hintOnly` (final assessment, or a question card is open): every entry becomes a spoiler-free nudge instead of its answer;
        /// `question` is the open card's concept, used when an entry has no memory line of its own.
        /// `task`: what the story is waiting on the learner to do right now (Hud.CurrentTask); Help / What next say exactly that.
        public static string Reply(IntentRouter.Result r, Beat beat, bool hub, string context, ClueBoard clues, bool hintOnly = false, Concept? question = null, string task = null)
        {
            switch (r.intent)
            {
                case Intent.Greeting:
                    return r.topic switch
                    {
                        "who" => "I'm Dr. Curio — botanist, scientific investigator and your partner on this case. I can explain anything you see, and I can take you places inside this plant.",
                        "thanks" => "You're welcome! Investigations are always better with a partner.",
                        "bye" => "Bye for now! I'll be right here if you need me.",
                        _ => "Hello, investigator! Ask me anything about this plant — try “What am I looking at?” or “Show me water.”"
                    };
                case Intent.Hint: return question == null && r.topic != null && Kb.ContainsKey(r.topic) ? Nudge(Kb[r.topic], null, beat) : Hint(question, beat);
                case Intent.Help:
                    return "Ask me about anything you see — like “What is xylem?” or “Why does the plant need this?” I can also take you places: try “Show me water.” " + (task != null ? "Right now: " + task : Next(hintOnly ? Beat.Assessment : beat, hub));
                case Intent.WhatNext: return task != null ? "Right now: " + task : Next(hintOnly ? Beat.Assessment : beat, hub);   // "answer the card" rather than a beat nudge that names the answer
                case Intent.WhatIsThis: return question != null ? NoSpoiler(question, beat) : Look(beat, hub);
                case Intent.ExplainAgain: return hintOnly ? "Of course! " + Hint(question, beat) : "Of course! Here's the short version: " + Explain(Focus(beat), beat);
                case Intent.WhyNeed:
                    return Why(r.topic == "food" ? "glucose" : r.topic == null || !Kb.ContainsKey(r.topic) ? Focus(beat) : r.topic, beat, hintOnly, question);
                case Intent.WherePhotosynthesis: return Explain("where", beat, hintOnly, question);
                case Intent.None:
                    if (r.topic == "clues")
                        return beat == Beat.Assessment ? "No spoilers mid-investigation! Think back to the three clues we unlocked, and what each one did for the food factory."
                             : hintOnly ? "No spoilers mid-investigation! " + Hint(question, beat) : Clues(clues);
                    return r.topic != null && Kb.ContainsKey(r.topic) ? Explain(r.topic, beat, hintOnly, question) : Fallback(context);
                default:
                    return r.topic != null && Kb.ContainsKey(r.topic) ? Explain(r.topic, beat, hintOnly, question) : question != null ? NoSpoiler(question, beat) : Look(beat, hub);
            }
        }

        /// Spoiler-free hint for a question about `c` (no concept → the current beat's focus): the tease before its reveal
        /// (prediction questions come before the reveal), otherwise a nudge towards what the learner has already seen.
        public static string Hint(Concept? c, Beat beat)
        {
            string key = c == null ? Focus(beat) : c.Value switch
            {
                Concept.Stomata => "stomata", Concept.Water => "water", Concept.Xylem => "xylem", Concept.Sunlight => "sunlight",
                Concept.Chloroplast => "chloroplast", Concept.Chlorophyll => "chlorophyll", Concept.Glucose => "glucose",
                Concept.Oxygen => "oxygen", Concept.Phloem => "phloem", _ => "equation"
            };
            var e = Kb[key];
            if (beat < e.reveal && e.tease != null) return e.tease;
            return e.memory != null ? $"Here's a hint: think back to {e.memory}." : "Here's a hint: think back to everything we've seen on our journey so far — the answer is in there.";
        }

        /// Spoken version: drops (formulas) and names the molecules. Dialogue.SayText shows the same string as the subtitle,
        /// so it must read well as well as sound right (no "C O 2" spellings).
        public static string Spoken(string s)
        {
            s = Regex.Replace(s, @"\s*\([^)]*[₀-₉→][^)]*\)", "");
            return s.Replace("C₆H₁₂O₆", "glucose").Replace("CO₂", "carbon dioxide").Replace("H₂O", "water").Replace("O₂", "oxygen")
                    .Replace(" → ", " makes ").Replace("→", " makes ").Replace("  ", " ").Trim();
        }

        // ------------------------------------------------------------------ composition
        static string Explain(string key, Beat beat, bool hintOnly = false, Concept? question = null)
        {
            var e = Kb[key];
            if (hintOnly) return Nudge(e, question, beat);
            return beat < e.reveal && e.tease != null ? e.tease : e.explain;
        }

        static string Why(string key, Beat beat, bool hintOnly = false, Concept? question = null)
        {
            var e = Kb[key];
            if (hintOnly) return Nudge(e, question, beat);
            return beat < e.reveal && e.tease != null ? e.tease : e.why;
        }

        /// Hint-only mode: never the entry's answer. The pre-reveal tease is spoiler-free by design; after the reveal a memory
        /// nudge, or (entries without one: stem, leaf, cell, minerals) the open question's own hint.
        static string Nudge(Entry e, Concept? question, Beat beat)
        {
            if (beat < e.reveal && e.tease != null) return e.tease;
            if (e.memory != null) return $"No spoilers mid-investigation! Think back to {e.memory} — you've already seen the answer.";
            return "No spoilers mid-investigation! " + Hint(question, beat);
        }

        /// "What am I looking at?" while a card is open: the scene itself is usually the answer.
        static string NoSpoiler(Concept? question, Beat beat) => "I'd be giving the game away if I described this now! " + Hint(question, beat);

        static string Clues(ClueBoard c)
        {
            if (c == null || c.AllFound) return "Photosynthesis needs three things: carbon dioxide and water are the raw materials, and sunlight provides the energy.";
            var found = new List<string>();
            if (c[ClueId.CO2] == ClueState.Discovered) found.Add("carbon dioxide");
            if (c[ClueId.Water] == ClueState.Discovered) found.Add("water");
            return found.Count switch
            {
                0 => "Photosynthesis needs three important things — and I'm not giving them away that easily! Find the clues and you'll know them all.",
                1 => $"So far we've found {found[0]}. Two more clues to go!",
                _ => $"So far we've found {found[0]} and {found[1]}. One more clue to go — and it's the one that powers everything."
            };
        }

        static string Fallback(string context)
        {
            var s = "Hmm, my scanner can't quite make sense of that one! I'm best with plant questions — try “What is xylem?”, “Where does oxygen go?” or “What am I looking at?”";
            return string.IsNullOrEmpty(context) ? s : s + $" Right now we're investigating {context}.";
        }

        /// The ingredient or structure the current beat is about (for "why does the plant need this?" / "explain again").
        static string Focus(Beat b) => b switch
        {
            Beat.Clue1 or Beat.CO2 => "co2",
            Beat.Stomata => "stomata",
            Beat.Clue2 or Beat.Water => "water",
            Beat.Roots => "roots",
            Beat.XylemRide => "xylem",
            Beat.Clue3 or Beat.Sunlight => "sunlight",
            Beat.EnterLeaf => "leaf",
            Beat.PlantCell => "chloroplast",
            Beat.Chloroplast => "chlorophyll",
            Beat.Reaction or Beat.Glucose => "glucose",
            Beat.Oxygen => "oxygen",
            Beat.Phloem => "phloem",
            Beat.Equation => "equation",
            _ => "photosynthesis"
        };

        /// "What am I looking at?" — describes the current scene.
        public static string Look(Beat b, bool hub) => b switch
        {
            Beat.Clue1 or Beat.Clue2 or Beat.Clue3 when hub => "We're back in the Botanical Investigation Chamber with our giant plant. The cards at the top are the clue tracker — select the next clue to keep investigating.",
            Beat.Scene2Start => "This is the Botanical Investigation Chamber forming around you — a living laboratory with one enormous plant at its heart.",
            Beat.Chamber => "You're in the Botanical Investigation Chamber. The giant plant in the middle is our case — drag to look around it, and scroll to zoom.",
            Beat.CurioIntro => "That's me, Dr. Curio — and that's the plant we're investigating. It looks peaceful, but somewhere inside it, food is being made.",
            Beat.Clue1 => "That's our giant plant and the three-clue tracker. Clue one is about something the plant needs that's all around us.",
            Beat.Stomata => "We're looking at the underside of a leaf, magnified a hundred times. Those little mouth-shaped openings are stomata, and the two curved cells around each one are guard cells.",
            Beat.CO2 => "Those small molecules drifting in are carbon dioxide — one carbon atom joined to two oxygen atoms. They're entering the leaf through the stoma.",
            Beat.Clue2 => "We're scanning the plant from top to bottom for the second clue. The signal is coming from below — under the floor.",
            Beat.Roots => "Those are the roots, hidden beneath the chamber floor. They spread through the soil, searching for water.",
            Beat.Water => "We're down in the soil, right beside a root hair. The blue molecules are water, waiting to be absorbed.",
            Beat.XylemRide => "We're riding inside the xylem! It's a narrow tube that carries water up from the roots, through the stem, to the leaves.",
            Beat.Clue3 => "This leaf has carbon dioxide and water — but its food factory isn't running. Something is still missing.",
            Beat.Sunlight => "That beam is sunlight striking the leaf. Its light energy is what wakes up the food factory.",
            Beat.EnterLeaf => "We're heading inside the leaf. Under its surface are layers of living cells packed tightly together.",
            Beat.PlantCell => "We're inside a plant cell. You can see the cell wall, a big vacuole full of watery sap, the nucleus — and those glowing green structures.",
            Beat.Chloroplast => "We're inside a chloroplast! Those green stacks are grana, packed with chlorophyll that soaks up light energy.",
            Beat.Reaction => "This is the reaction itself. Carbon dioxide and water are being rearranged, powered by light energy, into a brand-new molecule.",
            Beat.Glucose => "That golden molecule is glucose — the food the plant just made, packed with chemical energy.",
            Beat.Oxygen => "Those are oxygen molecules, the other product. Watch them travel out of the cell and leave through the stoma.",
            Beat.Phloem => "The orange channels are phloem. They carry food from the leaves to the roots, shoots, flowers and storage areas.",
            Beat.Equation => "This is the photosynthesis equation: carbon dioxide plus water, powered by light energy, makes glucose plus oxygen.",
            Beat.FinalPlant => "This is the whole plant with everything running at once: water climbing the xylem, carbon dioxide coming in, oxygen going out, and food flowing through the phloem.",
            Beat.Assessment => "This is our final investigation — a few questions to show how much of the food factory you can explain.",
            Beat.Complete => "Case closed! You've finished the investigation. You can explore the whole plant freely now.",
            _ => "The whole plant is yours to explore. Pick a destination, or follow water, carbon dioxide, oxygen or food on its journey."
        };

        /// "What should I do now?"
        public static string Next(Beat b, bool hub) => b switch
        {
            Beat.Scene2Start => "Just watch for a moment — the chamber is still forming around us.",
            Beat.Chamber => "Look around! Drag to rotate, scroll to zoom and right-drag to pan.",
            Beat.CurioIntro => "Listen in — I'm explaining our mission.",
            Beat.Clue1 when hub => "Select clue one on the tracker to begin — or just ask me to take you to carbon dioxide.",
            Beat.Clue2 when hub => "Select clue two to follow the water trail — or just say “Show me water.”",
            Beat.Clue3 when hub => "Select clue three and think about what the factory still needs.",
            Beat.Clue1 or Beat.Clue2 => "Follow along — we're investigating the clue right now.",
            Beat.Clue3 => "Carbon dioxide and water are here — what else could the factory need?",
            Beat.Stomata => "Watch the leaf surface closely — we're zooming in on a stoma.",
            Beat.CO2 => "Watch the carbon dioxide molecule slip in through the stoma.",
            Beat.Roots => "Follow the signal down into the roots.",
            Beat.Water => "Click a water molecule near the root hair and watch the root absorb it.",
            Beat.XylemRide => "Hold on and enjoy the ride — root, stem, branch, leaf!",
            Beat.Sunlight => "Watch what happens when sunlight reaches the leaf.",
            Beat.EnterLeaf => "Press Enter Leaf and we'll dive inside.",
            Beat.PlantCell => "Look for the green structures — that's where the action is.",
            Beat.Chloroplast => "Watch the light strike the chlorophyll inside the chloroplast.",
            Beat.Reaction => "Click the glowing carbon dioxide and water molecules to send them in — then watch them combine.",
            Beat.Glucose => "Watch how the plant uses its glucose: energy, growth and storage.",
            Beat.Oxygen => "Follow the oxygen out of the cell and through the stoma.",
            Beat.Phloem => "Watch the food travel through the phloem, then try the xylem and phloem switches.",
            Beat.Equation => "Watch the equation build — every part of it is something you discovered.",
            Beat.FinalPlant => "Rotate the plant and watch every process running at once.",
            Beat.Assessment => "Answer the question on the card — take your time and think back to what you saw.",
            Beat.Complete => "Choose Free Explore to keep investigating, or Exit when you're done.",
            _ => "Pick a destination at the bottom of the screen — or ask me to show you water, the stomata, a chloroplast or the phloem."
        };
    }
}
