using System.Collections.Generic;
using System.Text;

namespace Photosynthesis
{
    /// Offline keyword/phrase router: learner text → Intent + knowledge topic. Tolerates typos (edit distance),
    /// CO2/co₂/carbon-dioxide variants, contractions and filler words. Pure function, no Unity state.
    public static class IntentRouter
    {
        public readonly struct Result
        {
            public readonly Intent intent;
            /// Knowledge-base key ("co2", "water", … "cell", "leaf") or greeting kind ("hello", "who", "thanks", "bye"); null if none.
            public readonly string topic;
            /// Explicit "show me / take me to / where is …" request. A plain "what is X?" is answered, not navigated.
            public readonly bool show;
            public Result(Intent i, string t, bool s = false) { intent = i; topic = t; show = s; }
        }

        public static Result Parse(string text)
        {
            var s = Normalize(text);
            if (s.Length == 0) return new Result(Intent.None, null);
            var words = s.Split(' ');
            var keys = new List<string>(words.Length);
            foreach (var w in words) keys.Add(Canonical(w));
            string p = " " + string.Join(" ", keys) + " ";   // canonical sentence, space-padded for phrase tests
            bool Has(string phrase) => p.Contains(" " + phrase + " ");
            bool Any(params string[] phrases) { foreach (var x in phrases) if (Has(x)) return true; return false; }

            string topic = Topic(keys, p);
            bool why = Has("why");
            bool where = Any("where", "wheres");
            bool show = where || Any("show", "go to", "zoom", "follow", "find", "point", "highlight", "visit", "see", "watch", "look at", "fly", "travel to", "explore", "inside", "enter", "dive");
            bool store = Any("store", "stored", "stores", "storage", "storing");   // "where is food stored?" is a phloem question, not a factory one

            // greetings / small talk (only when the message isn't really a question about something)
            if (topic == null && keys.Count <= 6)
            {
                if (Any("who are you", "your name", "who is curio", "who is dr curio")) return new Result(Intent.Greeting, "who");
                if (Any("thanks", "thank you", "thank", "thx", "cheers")) return new Result(Intent.Greeting, "thanks");
                if (Any("bye", "goodbye", "see you", "see ya")) return new Result(Intent.Greeting, "bye");
                if (Any("hi", "hello", "hey", "hiya", "yo", "howdy", "good morning", "good afternoon", "good evening", "how are you", "sup"))
                    return new Result(Intent.Greeting, "hello");
            }

            if (Any("hint", "hints")) return new Result(Intent.Hint, topic);   // the question card's button sends "Can you give me a hint?"

            if (topic == null && Any("again", "repeat", "once more", "say that", "what did you say", "come again", "pardon", "did not get", "did not catch", "did not hear",
                                     "do not understand", "do not get it", "confused", "confusing", "explain", "makes no sense", "lost me"))
                return new Result(Intent.ExplainAgain, null);

            if (Any("what next", "what is next", "what now", "next step", "what do i do", "what should i do", "what am i supposed", "what do we do", "where do i go",
                    "where should i go", "how do i continue", "how do i go on", "what to do", "how do i start", "what do i click", "what do i press")
                || topic == null && Any("stuck", "lost", "continue", "proceed", "next", "now what"))
                return new Result(Intent.WhatNext, null);

            if (topic == null && Any("help", "what can you do", "what can i ask", "how does this work", "how do i use", "instructions", "controls", "how to play"))
                return new Result(Intent.Help, null);

            if (Has("green") && (why || Any("colour", "color", "coloured", "colored")) && (topic == null || topic == "leaf" || topic == "cell" || topic == "chloroplast" || topic == "chlorophyll"))
                return new Result(Intent.ShowChlorophyll, "chlorophyll");   // "why are leaves green?"

            if (why) return new Result(Intent.WhyNeed, topic);

            if (topic == "photosynthesis" && where || (topic == "food" && (where || Any("made", "make", "makes", "produced", "factory")) && !Moves(p) && !store)
                || Any("food factory", "where is food made", "where does it happen"))
                return new Result(Intent.WherePhotosynthesis, "where", true);

            switch (topic)
            {
                case "co2": return new Result(Intent.ShowCO2, topic, show);
                case "stomata": return new Result(Intent.ShowStomata, topic, show);
                case "water": return new Result(Intent.ShowWater, topic, show);
                case "roots": return new Result(Intent.ShowRoots, topic, show);
                case "xylem": return new Result(Intent.ShowXylem, topic, show);
                case "sunlight": return new Result(Intent.ShowSunlight, topic, show);
                case "chloroplast": return new Result(Intent.ShowChloroplast, topic, show);
                case "chlorophyll": return new Result(Intent.ShowChlorophyll, topic, show);
                case "glucose": return new Result(Intent.ShowGlucose, topic, show);
                case "oxygen": return new Result(Intent.ShowOxygen, topic, show);
                case "phloem": return new Result(Intent.ShowPhloem, topic, show);
                case "equation": return new Result(Intent.ShowEquation, topic, show);
                case "leaf": return new Result(Intent.ShowLeaf, topic, show);
                case "cell": return new Result(Intent.ShowCell, topic, show);
                case "stem": return new Result(Intent.ShowStem, topic, show);
                case "food": { bool viaPhloem = Moves(p) || store; return new Result(viaPhloem ? Intent.ShowPhloem : Intent.ShowGlucose, viaPhloem ? "phloem" : "glucose", show); }
            }

            if (Any("what am i looking at", "what are we looking at", "what is this", "what is that", "what are these", "what are those", "what is it",
                    "where am i", "where are we", "what is happening", "what is going on", "what happens here", "describe", "look at", "this", "that", "these", "here"))
                return new Result(Intent.WhatIsThis, topic);

            return new Result(Intent.None, topic);   // topic may still be a non-world KB key ("cell", "leaf", "photosynthesis" …)
        }

        // whole-word " go " etc. so "good" / "got" don't count as movement
        static bool Moves(string p) => p.Contains(" go ") || p.Contains(" goes ") || p.Contains(" going ") || p.Contains(" gone ") || p.Contains(" travel")
                                      || p.Contains(" transport") || p.Contains(" move") || p.Contains(" carr") || p.Contains(" deliver") || p.Contains(" sent ")
                                      || p.Contains(" send") || p.Contains(" reach");

        /// First topic by priority (specific structures before generic ingredients), not by position in the sentence.
        static readonly string[] Priority = { "equation", "stomata", "chlorophyll", "chloroplast", "xylem", "phloem", "glucose", "oxygen", "co2", "roots", "water", "sunlight", "food", "photosynthesis", "minerals", "stem", "cell", "leaf" };
        static string Topic(List<string> keys, string p)
        {
            if (keys.Contains("food") && keys.Contains("roots") && !Moves(p)) return "soilfood";   // "do plants eat soil?" — the Scene 1 misconception
            foreach (var t in Priority) if (keys.Contains(t)) return t;
            if (p.Contains(" ingredient") || p.Contains(" clue") || p.Contains(" plants need ") || p.Contains(" plant need ") || p.Contains(" it need ")) return "clues";
            // "which gas goes in?" → CO₂; "which gas does the plant give off / release / let out?" → oxygen (" leav" never matches the canonical " leaf ")
            if (p.Contains(" air ") || p.Contains(" gas "))
                return p.Contains(" give") || p.Contains(" release") || p.Contains(" produc") || p.Contains(" make") || p.Contains(" made ") || p.Contains(" out ")
                       || p.Contains(" leav") || p.Contains(" escape") || p.Contains(" exit") ? "oxygen" : "co2";
            return null;
        }

        // ------------------------------------------------------------------ normalisation
        static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var sb = new StringBuilder(text.Length + 8);
            foreach (var ch in text.ToLowerInvariant())
            {
                char c = ch;
                if (c >= '₀' && c <= '₉') c = (char)('0' + (c - '₀'));   // subscripts ₀–₉
                else if (c == '’' || c == '‘') c = '\'';
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') sb.Append(c);
                else if (c == '\'') continue;                                          // what's → whats, don't → dont
                else sb.Append(' ');
            }
            var s = " " + sb + " ";
            foreach (var (a, b) in Phrases) s = s.Replace(a, b);
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }

        // Multi-word variants collapsed before tokenising (space-padded so they only match whole words).
        static readonly (string, string)[] Phrases =
        {
            (" carbon di oxide ", " co2 "), (" carbon dioxide ", " co2 "), (" carbon dioxides ", " co2 "), (" c o 2 ", " co2 "), (" co 2 ", " co2 "), (" c02 ", " co2 "),
            (" h 2 o ", " h2o "), (" o 2 ", " o2 "), (" guard cells ", " stomata "), (" guard cell ", " stomata "), (" root hairs ", " roots "), (" root hair ", " roots "),
            (" word equation ", " equation "), (" chemical equation ", " equation "), (" sun light ", " sunlight "), (" sun shine ", " sunlight "),
            (" photo synthesis ", " photosynthesis "), (" cell wall ", " cell "), (" dr curio ", " curio "), (" doctor curio ", " curio "),
            (" what s ", " what is "), (" whats ", " what is "), (" where s ", " where is "), (" wheres ", " where is "), (" dont ", " do not "), (" didnt ", " did not "),
            (" doesnt ", " does not "), (" cant ", " can not "), (" cannot ", " can not "), (" i m ", " im "), (" wat ", " what "), (" wot ", " what "), (" wht ", " what "),
            (" wut ", " what "), (" pls ", " please "), (" plz ", " please "), (" u ", " you "), (" r ", " are "), (" y ", " why "), (" whyy ", " why "),
            (" take me to ", " show "), (" bring me to ", " show "), (" show me ", " show "), (" can you show ", " show "), (" let me see ", " show "),
            (" take me ", " show "), (" bring me ", " show "),   // "take me inside the leaf" (after the "… to" forms above)
        };

        // ------------------------------------------------------------------ vocabulary
        static readonly Dictionary<string, string> Alias = new Dictionary<string, string>
        {
            ["co2"] = "co2", ["carbondioxide"] = "co2", ["dioxide"] = "co2", ["carbon"] = "co2", ["c02"] = "co2",
            ["stomata"] = "stomata", ["stoma"] = "stomata", ["stomate"] = "stomata", ["stomates"] = "stomata", ["stomatas"] = "stomata", ["stomatta"] = "stomata",
            ["pore"] = "stomata", ["pores"] = "stomata", ["hole"] = "stomata", ["holes"] = "stomata", ["opening"] = "stomata", ["openings"] = "stomata", ["doorway"] = "stomata",
            ["xylem"] = "xylem", ["zylem"] = "xylem", ["xylum"] = "xylem", ["xylam"] = "xylem", ["zylum"] = "xylem", ["xyleme"] = "xylem", ["xilem"] = "xylem", ["zilem"] = "xylem",
            ["phloem"] = "phloem", ["floem"] = "phloem", ["flowem"] = "phloem", ["phlowem"] = "phloem", ["phleom"] = "phloem", ["phlome"] = "phloem", ["flome"] = "phloem", ["phlem"] = "phloem",
            ["chlorophyll"] = "chlorophyll", ["chlorophyl"] = "chlorophyll", ["clorophyll"] = "chlorophyll", ["clorophyl"] = "chlorophyll", ["clorofil"] = "chlorophyll", ["chlorofil"] = "chlorophyll", ["pigment"] = "chlorophyll",
            ["chloroplast"] = "chloroplast", ["chloroplasts"] = "chloroplast", ["cloroplast"] = "chloroplast", ["cloroplasts"] = "chloroplast", ["plastid"] = "chloroplast", ["grana"] = "chloroplast", ["thylakoid"] = "chloroplast", ["thylakoids"] = "chloroplast",
            ["glucose"] = "glucose", ["sugar"] = "glucose", ["sugars"] = "glucose", ["starch"] = "glucose", ["carbohydrate"] = "glucose", ["carbohydrates"] = "glucose", ["c6h12o6"] = "glucose", ["glucoes"] = "glucose",
            ["oxygen"] = "oxygen", ["o2"] = "oxygen", ["oxigen"] = "oxygen", ["oxegen"] = "oxygen", ["breathe"] = "oxygen", ["breath"] = "oxygen", ["breathing"] = "oxygen",
            ["root"] = "roots", ["roots"] = "roots", ["soil"] = "roots", ["underground"] = "roots", ["dirt"] = "roots", ["ground"] = "roots", ["rootes"] = "roots",
            ["water"] = "water", ["h2o"] = "water", ["watter"] = "water", ["wter"] = "water", ["drink"] = "water", ["drinks"] = "water", ["thirsty"] = "water",
            ["sunlight"] = "sunlight", ["sun"] = "sunlight", ["light"] = "sunlight", ["sunshine"] = "sunlight", ["sunbeam"] = "sunlight", ["sunlite"] = "sunlight", ["energy"] = "sunlight", ["lite"] = "sunlight",
            ["equation"] = "equation", ["formula"] = "equation", ["equasion"] = "equation", ["equations"] = "equation", ["reaction"] = "equation",
            ["food"] = "food", ["foods"] = "food", ["eat"] = "food", ["eats"] = "food", ["meal"] = "food",
            ["photosynthesis"] = "photosynthesis", ["photosynthesise"] = "photosynthesis", ["photosynthesize"] = "photosynthesis", ["fotosynthesis"] = "photosynthesis",
            ["mineral"] = "minerals", ["minerals"] = "minerals", ["nutrient"] = "minerals", ["nutrients"] = "minerals",
            ["stem"] = "stem", ["stems"] = "stem", ["stalk"] = "stem", ["trunk"] = "stem",
            ["cell"] = "cell", ["cells"] = "cell", ["nucleus"] = "cell", ["vacuole"] = "cell", ["cytoplasm"] = "cell",
            ["leaf"] = "leaf", ["leaves"] = "leaf", ["leafs"] = "leaf",
        };

        // Long words worth fuzzy-matching. Short/common words (water, light, sun, root) are alias-only to avoid "later" → water.
        static readonly string[] Fuzzy = { "stomata", "xylem", "phloem", "chlorophyll", "chloroplast", "glucose", "oxygen", "dioxide", "carbon", "photosynthesis",
                                           "sunlight", "equation", "carbohydrate", "vacuole", "nucleus", "minerals", "chloroplasts", "underground" };

        static string Canonical(string w)
        {
            if (Alias.TryGetValue(w, out var k)) return k;
            if (w.Length >= 4 && !char.IsDigit(w[0]))
            {
                string best = null; int bestD = 99;
                foreach (var f in Fuzzy)
                {
                    int max = f.Length >= 9 ? 2 : f.Length >= 5 ? 1 : 0;
                    if (System.Math.Abs(f.Length - w.Length) > max) continue;
                    int d = Distance(w, f, max);
                    if (d <= max && d < bestD) { bestD = d; best = f; }
                }
                if (best != null) return Alias[best];
            }
            return w;
        }

        /// Optimal-string-alignment distance (Levenshtein + adjacent swaps), early-out above `max`.
        static int Distance(string a, string b, int max)
        {
            int n = a.Length, m = b.Length;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                int rowMin = int.MaxValue;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = System.Math.Min(System.Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = System.Math.Min(v, d[i - 2, j - 2] + 1);
                    d[i, j] = v; if (v < rowMin) rowMin = v;
                }
                if (rowMin > max) return max + 1;
            }
            return d[n, m];
        }
    }
}
