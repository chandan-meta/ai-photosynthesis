using System;
using UnityEngine;

namespace Photosynthesis
{
    /// Story beats in play order (storyboard Scenes 2–15). Also the Debug panel jump list.
    public enum Beat
    {
        Scene2Start, Chamber, CurioIntro,
        Clue1, Stomata, CO2,
        Clue2, Roots, Water, XylemRide,
        Clue3, Sunlight,
        EnterLeaf, PlantCell, Chloroplast, Reaction, Glucose, Oxygen,
        Phloem, Equation, FinalPlant, Assessment, Complete, FreeExplore
    }

    public enum ClueId { CO2, Water, Sunlight }
    public enum ClueState { Locked, Available, Discovered }
    public enum Tone { Cyan, Gold, Green, Blue, Orange }
    public enum ScanState { Scanning, Detected, Complete }
    public enum Level { Struggling, Average, High }
    public enum PlantPart { WholePlant, Roots, Stem, Leaf, Stomata, Xylem, Phloem, Cell, Chloroplast }
    /// Processes the hero plant can visualise (final working plant, free explore "Follow …", reteach demos).
    public enum Flow { Water, CO2, Sunlight, Chloroplast, Glucose, Phloem, O2 }
    public enum CurioEmote { Idle, Think, Surprise, Celebrate, Nod, Point }

    public enum IconId
    {
        Menu, Reset, Help, Close, Mic, Send, ChevronRight, ChevronLeft, ChevronDown, Plus, Minus, Zoom, Check, Lock, Unlock,
        Leaf, Drop, Sun, CO2, O2, Glucose, Scan, Settings, Pause, Play, Replay, Info, Sparkle, CC, Eye,
        Plant, Root, Stem, Xylem, Phloem, Stomata, Cell, Chloroplast, Energy, Target, ArrowUp, ArrowDown, Bug, Exit
    }

    public enum Sfx
    {
        UiHover, UiClick, UiSelect, Unlock, Success, Wrong, Scan, Beep, TargetLock, AnalysisComplete,
        Whoosh, Materialize, WaterAbsorb, WaterRush, EnergyBuild, SunImpact, ChloroplastActivate,
        GlucoseDiscover, OxygenRelease, Sparkle, Pop, FinaleCadence
    }

    public enum MusicState { None, NewWorld, Investigation, Microscopic, WaterJourney, SunlightReveal, Reaction, Resolution, FreeExplore }

    public enum QType { Recall, Understanding, Prediction, Discovery, Application, Reasoning, Integrated }
    public enum Concept { Stomata, Water, Xylem, Sunlight, Chloroplast, Chlorophyll, Glucose, Oxygen, Phloem, Equation, Integration }

    /// One MCQ from the storyboard bank (Q1–Q20).
    [Serializable]
    public class Question
    {
        public string id;
        public QType type;
        public int tier;            // 1 basic recall · 2 understanding/prediction · 3 application/reasoning
        public Concept concept;
        public string lead;         // Dr. Curio's lead-in line (may be empty)
        public string text;
        public string[] options;
        public int correct;
        public string correctFeedback;
        public string wrongFeedback;
        public bool prediction => type == QType.Prediction || type == QType.Discovery;
    }

    /// Figma tokens (file "Photosynthesis — Hidden Food Factory · UI System"), shared by UI and 3D.
    public static class Palette
    {
        public static readonly Color Scene = Hex(0x06120E), Glass = Hex(0x0A1C17), Raised = Hex(0x123229), Sunken = Hex(0x050E0B);
        public static readonly Color GlassStroke = Hex(0x7FE6CF), Green = Hex(0x1F8A55), GreenBright = Hex(0x4BE08A), GreenDeep = Hex(0x0F4D33);
        public static readonly Color Cyan = Hex(0x3DD6E8), Gold = Hex(0xF5C451), GoldDeep = Hex(0xC98D16);
        public static readonly Color Text = Color.white, TextSecondary = Hex(0xCFE5DD), TextMuted = Hex(0x86A39A), OnGold = Hex(0x1E1505);
        public static readonly Color Correct = Hex(0x4CD68E), Incorrect = Hex(0xFF6B6B), Locked = Hex(0x5E6E69);
        public static readonly Color Water = Hex(0x45A8FF), CO2 = Hex(0xA9BACB), Oxygen = Hex(0x9BE7FF), Glucose = Hex(0xFFB547);
        public static readonly Color Xylem = Hex(0x3D9BFF), Phloem = Hex(0xFF9F43), Light = Hex(0xFFD866);
        // Atoms (CPK-ish, softened)
        public static readonly Color Carbon = Hex(0x3B4048), OxygenAtom = Hex(0xD92E2E), Hydrogen = Hex(0xF2F6FA);

        public static Color Hex(int rgb, float a = 1f) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
        public static Color Of(Tone t) => t switch { Tone.Gold => Gold, Tone.Green => GreenBright, Tone.Blue => Water, Tone.Orange => Phloem, _ => Cyan };
        public static Color Of(ClueId c) => c switch { ClueId.Water => Water, ClueId.Sunlight => Gold, _ => GreenBright };
    }
}
