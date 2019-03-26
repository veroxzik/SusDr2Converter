using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SusDr2Converter
{
    class Program
    {
        static void Main(string[] args)
        {
            string command = "";
            bool filesInFolder = false;
            bool recursive = false;
            bool fileIsValid = false;
            List<string> filenames = new List<string>();
            if (args.Length == 0 || args[0].Contains("--?") || args[0].Contains("/?") || args[0].Contains("--help"))
            {
                Console.WriteLine();
                Console.WriteLine("Welcome to the SUS <-> DR2 Converter! (Version 0.1)");
                Console.WriteLine();
                Console.WriteLine("For the time being, only SUS->DR2 conversion is possible.");
                Console.WriteLine();
                Console.WriteLine("To convert a single file, type the path here now (include file extension), or run the .exe with the path as the first and only argument.");
                Console.WriteLine("To convert all files in this folder, type \"folder\", or run the .exe with the argument --f");
                Console.WriteLine("To convert all files in this folder and every sub-folder recursively, type \"recursive\", or run the .exe with the argument --r");
                Console.WriteLine();
                Console.WriteLine("**WARNING** This program will overwrite existing conversions. Back them up if you do not wish to lose them.");
                Console.WriteLine();
                Console.WriteLine("Otherwise, press Enter to close.");
                command = Console.ReadLine();

                if (string.IsNullOrEmpty(command))
                    Environment.Exit(0);
            }

            if (command.ToLower() == "folder" || (args.Length > 0 && args[0].ToLower() == "--f"))
                filesInFolder = true;
            else if (command.ToLower() == "recursive" || (args.Length > 0 && args[0].ToLower() == "--r"))
                recursive = true;
            else
            {
                if (string.IsNullOrEmpty(command) && args.Length > 0)
                    command = args[0];
                if (!File.Exists(command))
                {
                    Console.WriteLine("The specified file {0} cannot be found. Press Enter to close.", command);
                    Console.ReadLine();
                    Environment.Exit(0);
                }

                filenames.Add(command);
                fileIsValid = true;
            }

            if(recursive)
            {
                var filesHere = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory);
                foreach (var file in filesHere)
                {
                    if (file.EndsWith(".sus"))
                        filenames.Add(file);
                }
                SearchForFile(ref filenames, AppDomain.CurrentDomain.BaseDirectory, ".sus");
            }

            if(filesInFolder)
            {
                var files = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory);
                foreach (var file in files)
                {
                    if (file.EndsWith(".sus"))
                        filenames.Add(file);
                }
            }

            foreach (var file in filenames)
            {
                Console.WriteLine("Parsing {0}...", file);

                // Check which direction we're going
                if (file.EndsWith(".sus"))
                {
                    Dr2Metadata meta;
                    var notes = ParseSus(file, out meta);
                    string exportName = file.Replace(".sus", ".dr2");
                    ExportDr2(exportName, notes, meta);
                    Globals.ResetFlags();
                    NoteIncrementer.Reset();
                }
                else if (file.EndsWith(".dr2"))
                {
                    Console.WriteLine("DR2->SUS is not yet supported.");
                }
                Console.WriteLine("Parse is complete.");
                Console.WriteLine("");
            }
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }

        static List<Dr2Note> ParseSus(string filename, out Dr2Metadata metadata)
        {
            List<Dr2Note> dr2Notes = new List<Dr2Note>();
            metadata = new Dr2Metadata();
            Dictionary<int, Dr2BpmChange> bpmChanges = new Dictionary<int, Dr2BpmChange>();
            string[] bpmSplit = new string[] { "#BPM", ":" };

            Dictionary<int, int> holdIDlist = new Dictionary<int, int>();

            List<Tuple<NoteParse, int>> unmatchedAirs = new List<Tuple<NoteParse, int>>();
            List<NoteParse> unmatchedAirLane = new List<NoteParse>();

            Dictionary<int, SlideCollection> tempSlideDict = new Dictionary<int, SlideCollection>();
            List<SlideCollection> slideNotes = new List<SlideCollection>();

            List<NoteParse> failedNotes = new List<NoteParse>();

            int ticksPerBeat = 192; // Default value

            using (StreamReader sr = new StreamReader(filename))
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();

                    // Eventually need to parse metadata
                    if (line.StartsWith("#DESIGNER"))
                        metadata.Designer = line.Replace("#DESIGNER ", "").Replace("\"", "");
                    else if (line.StartsWith("#WAVEOFFSET"))
                        metadata.Offset = Convert.ToDouble(line.Replace("#WAVEOFFSET ", ""));
                    else if (Regex.Match(line, "(#BPM)").Success)
                    {
                        string[] split = line.Split(bpmSplit, StringSplitOptions.RemoveEmptyEntries);
                        int index = ParseAlphanumeric(split[0].Substring(1));
                        if (!bpmChanges.ContainsKey(index))
                            bpmChanges.Add(index, new Dr2BpmChange() { Bpm = Convert.ToDouble(split[1]), BpmStart = -1 });
                        else
                            Globals.MultipleBPMLinesPresent = true;
                        
                    }
                    else if (line.StartsWith("#DIFFICULTY"))
                    {
                        string[] split = line.Split(new string[] { "#DIFFICULTY", ":", "\"" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var numStr in split)
                        {
                            int num;
                            if (int.TryParse(numStr, out num))
                                metadata.Difficulty = num;
                        }
                    }
                    else if (line.StartsWith("#REQUEST"))
                    {
                        if(line.Contains("ticks_per_beat"))
                        {
                            string[] split = line.Split(new string[] { "#REQUEST", "\"", " " }, StringSplitOptions.RemoveEmptyEntries);
                            if (split[0] == "ticks_per_beat")
                                ticksPerBeat = Convert.ToInt32(split[1]);
                            else
                                Globals.ErrorParsingTicksPerBeat = true;
                        }
                    }

                    // Regex match for Single Notes
                    if (Regex.Match(line, "[#][0-9]{3}[1]").Success)
                    {
                        var parsed = ParseLine(line);
                        double noteSub = 1.0 / parsed.Notes.Count;
                        for (int i = 0; i < parsed.Notes.Count; i++)
                        {
                            switch (parsed.Notes[i].Item1)
                            {
                                case 1: // Tap
                                case 2: // ExTap
                                case 3: // Flick
                                    dr2Notes.Add(new Dr2Note()
                                    {
                                        ID = NoteIncrementer.GetNextID(),
                                        NoteType = SusToDr2Note(parsed.NoteClass, parsed.Notes[i].Item1),
                                        Measure = (parsed.Measure + i * noteSub),
                                        LaneIndex = parsed.LaneIndex,
                                        Width = parsed.Notes[i].Item2
                                    });
                                    break;
                                case 4: // Hell Tap
                                    break;
                                default: // Rest notes / spacers (0) are ignored
                                    break;
                            }
                        }
                    }
                    // Regex match for Hold, Slide, and Air-Action Lane
                    else if (Regex.IsMatch(line, "[#][0-9]{3}[2-4]"))
                    {
                        var parsed = ParseLine(line);
                        if (parsed.NoteClass == 4)
                        {
                            unmatchedAirLane.Add(parsed);
                        }
                        else if (parsed.NoteClass == 3)
                        {
                            double noteSub = 1.0 / parsed.Notes.Count;
                            for (int i = 0; i < parsed.Notes.Count; i++)
                            {
                                switch (parsed.Notes[i].Item1)
                                {
                                    case 1: // Start Note
                                    case 2: // End Note
                                        // Check if a collection for this already exists
                                        if (tempSlideDict.ContainsKey(parsed.NoteIdentifier))
                                        {
                                            // Check to see if it already has a start and end
                                            if (tempSlideDict[parsed.NoteIdentifier].containsStart && tempSlideDict[parsed.NoteIdentifier].containsEnd)
                                            {
                                                // Then commit this and start a new one
                                                slideNotes.Add(tempSlideDict[parsed.NoteIdentifier]);
                                                tempSlideDict[parsed.NoteIdentifier] = new SlideCollection();
                                            }
                                        }
                                        else
                                        {
                                            // Add a new one
                                            tempSlideDict.Add(parsed.NoteIdentifier, new SlideCollection());
                                        }
                                        // Add this note to it
                                        tempSlideDict[parsed.NoteIdentifier].Notes.Add(new Tuple<NoteParse, int>(parsed, i));
                                        break;
                                    case 3: // Bend Note Step
                                    case 5: // Bend Note Invisible
                                        if (!tempSlideDict.ContainsKey(parsed.NoteIdentifier))
                                            tempSlideDict.Add(parsed.NoteIdentifier, new SlideCollection());    // Add a new one
                                        // Add this note to it
                                        tempSlideDict[parsed.NoteIdentifier].Notes.Add(new Tuple<NoteParse, int>(parsed, i));
                                        break;
                                    case 4: // Bezier notes
                                        // DR2 doesn't support bezier notes
                                        if (!tempSlideDict.ContainsKey(parsed.NoteIdentifier))
                                            tempSlideDict.Add(parsed.NoteIdentifier, new SlideCollection());    // Add a new one
                                        // Add this note to it
                                        tempSlideDict[parsed.NoteIdentifier].Notes.Add(new Tuple<NoteParse, int>(parsed, i));
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                        else // This is only hold notes
                        {
                            double noteSub = 1.0 / parsed.Notes.Count;
                            for (int i = 0; i < parsed.Notes.Count; i++)
                            {
                                switch (parsed.Notes[i].Item1)
                                {
                                    case 1: // Start a new note
                                        dr2Notes.Add(new Dr2Note()
                                        {
                                            ID = NoteIncrementer.GetNextID(),
                                            NoteType = SusToDr2Note(parsed.NoteClass, parsed.Notes[i].Item1),
                                            Measure = (parsed.Measure + i * noteSub),
                                            LaneIndex = parsed.LaneIndex,
                                            Width = parsed.Notes[i].Item2
                                        });
                                        holdIDlist.Remove(parsed.NoteIdentifier);
                                        holdIDlist[parsed.NoteIdentifier] = dr2Notes.Last().ID;
                                        break;
                                    case 3: // Bend Point Step
                                    case 5: // Bend Point (Invisible)
                                        dr2Notes.Add(new Dr2Note()
                                        {
                                            ID = NoteIncrementer.GetNextID(),
                                            NoteType = SusToDr2Note(parsed.NoteClass, parsed.Notes[i].Item1),
                                            Measure = (parsed.Measure + i * noteSub),
                                            LaneIndex = parsed.LaneIndex,
                                            Width = parsed.Notes[i].Item2,
                                            ParentID = holdIDlist[parsed.NoteIdentifier]
                                        });
                                        holdIDlist[parsed.NoteIdentifier] = dr2Notes.Last().ID;
                                        break;
                                    case 2: // End a note
                                        dr2Notes.Add(new Dr2Note()
                                        {
                                            ID = NoteIncrementer.GetNextID(),
                                            NoteType = SusToDr2Note(parsed.NoteClass, parsed.Notes[i].Item1),
                                            Measure = (parsed.Measure + i * noteSub),
                                            LaneIndex = parsed.LaneIndex,
                                            Width = parsed.Notes[i].Item2,
                                            ParentID = holdIDlist[parsed.NoteIdentifier]
                                        });
                                        holdIDlist[parsed.NoteIdentifier] = dr2Notes.Last().ID;
                                        break;
                                    default:    // Rest notes / spacers (0) are ignored
                                        break;
                                }
                            }
                        }
                    }
                    // Regex match for Air Motions
                    else if (Regex.IsMatch(line, "[#][0-9]{3}[5]"))
                    {
                        // Air Motions are weird, because they must be paired with an existing note
                        var parsed = ParseLine(line);
                        double noteSub = 1.0 / parsed.Notes.Count;
                        for (int i = 0; i < parsed.Notes.Count; i++)
                        {
                            if (parsed.Notes[i].Item1 > 0)
                            {
                                int laneIndex = parsed.LaneIndex;
                                int width = parsed.Notes[i].Item2;
                                double measure = parsed.Measure + i * noteSub;
                                var noteIndex = dr2Notes.FindIndex(x => x.Measure == measure && x.LaneIndex == laneIndex && x.Width == width);
                                if (noteIndex >= 0)
                                    dr2Notes[noteIndex].AirDirection = SusToDr2Note(parsed.NoteClass, parsed.Notes[i].Item1);
                                else
                                    unmatchedAirs.Add(new Tuple<NoteParse, int>(parsed, i));
                            }
                        }
                    }
                    // Parse BPM changes
                    else if (Regex.IsMatch(line, "[#][0-9]{3}(08)"))
                    {
                        var parsed = ParseLine(line);
                        double noteSub = 1.0 / parsed.Notes.Count;
                        for (int i = 0; i < parsed.Notes.Count; i++)
                        {
                            if (parsed.Notes[i].Item2 > 0)
                            {
                                // Then there's a BPM change somewhere here
                                int bpmID = Convert.ToInt32(parsed.Notes[i].Item1.ToString() + parsed.Notes[i].Item2.ToString());
                                bpmChanges[bpmID].BpmStart = parsed.Measure + i * noteSub;
                            }
                        }
                    }
                    // Parse Time Signature
                    // ** DR2 does not support dynamic time signature changes, so we just grab the first one **
                    else if (Regex.IsMatch(line, "[#][0-9]{3}(02)"))
                    {
                        var parsed = ParseLine(line, true);

                        if (metadata.Beat == -1)
                            metadata.Beat = parsed.Values[0] / 4;
                    }
                    // Parse Speed Changes
                    else if (Regex.IsMatch(line, "(#TIL)"))
                    {
                        if (line.StartsWith("#TIL00"))
                        {
                            string[] split = line.Split(new string[] { "#TIL00", ":", ",", "\"", "'", " " }, StringSplitOptions.RemoveEmptyEntries);
                            if(split.Length == 0)
                            {
                                metadata.SpeedChanges.Add(new Dr2SpeedChange() { SpeedChange = 1.0, SpeedChangeIndex = 0.0 });
                            }
                            else if(split.Length % 3 != 0)
                            {
                                Globals.ErrorParsingSpeedChange = true;
                            }
                            else
                            {
                                for (int i = 0; i < split.Length; i+=3)
                                {
                                    double measure = Convert.ToDouble(split[i]) + Convert.ToDouble(split[i + 1]) / (ticksPerBeat * (metadata.Beat * 4));
                                    double speed = Convert.ToDouble(split[i + 2]);

                                    metadata.SpeedChanges.Add(new Dr2SpeedChange() { SpeedChange = speed, SpeedChangeIndex = measure });
                                }
                            }
                        }
                        else
                            Globals.ComplexSpeedChangesPresent = true;
                    }
                    // Attributes tag is not supported
                    else if(Regex.IsMatch(line, "(#ATR)") || Regex.IsMatch(line, "(#ATTR)"))
                    {
                        Globals.AttributesTagPresent = true;
                    }
                }
            }

            // Generate slide notes
            // Commit leftover notes
            foreach (var item in tempSlideDict)
            {
                slideNotes.Add(item.Value);
            }
            foreach (var list in slideNotes)
            {
                list.Notes.Sort((x, y) => (x.Item1.Measure + x.Item2 * (1.0 / x.Item1.Notes.Count)).CompareTo(y.Item1.Measure + y.Item2 * (1.0 / y.Item1.Notes.Count)));
                foreach (var tuple in list.Notes)
                {
                    var parsed = tuple.Item1;
                    double noteSub = 1.0 / parsed.Notes.Count;
                    var i = tuple.Item2;

                    bool isStart = parsed.Notes[i].Item1 == 1;

                    dr2Notes.Add(new Dr2Note()
                    {
                        ID = NoteIncrementer.GetNextID(),
                        NoteType = SusToDr2Note(parsed.NoteClass, parsed.Notes[i].Item1),
                        Measure = parsed.Measure + i * noteSub,
                        LaneIndex = parsed.LaneIndex,
                        Width = parsed.Notes[i].Item2,
                        ParentID = isStart ? 0 : dr2Notes.Count - 1
                    });
                }
            }

            // Try to match air notes again
            foreach (var item in unmatchedAirs)
            {
                var parsed = item.Item1;
                int i = item.Item2;
                double noteSub = 1.0 / parsed.Notes.Count;
                int laneIndex = parsed.LaneIndex;
                int width = parsed.Notes[i].Item2;
                double measure = parsed.Measure + i * noteSub;
                var noteIndex = dr2Notes.FindIndex(x => x.Measure == measure && x.LaneIndex == laneIndex && x.Width == width);
                if (noteIndex >= 0)
                    dr2Notes[noteIndex].AirDirection = SusToDr2Note(parsed.NoteClass, parsed.Notes[i].Item1);
                else
                    failedNotes.Add(parsed);
            }

            // Try to match air lanes
            foreach (var parsed in unmatchedAirLane)
            {
                double noteSub = 1.0 / parsed.Notes.Count;
                for (int i = 0; i < parsed.Notes.Count; i++)
                {
                    if(parsed.Notes[i].Item1 == 1)  // For start, we need to find the parent ID
                    {
                        int laneIndex = parsed.LaneIndex;
                        int width = parsed.Notes[i].Item2;
                        double measure = parsed.Measure + i * noteSub;
                        var noteIndex = dr2Notes.FindIndex(x => x.Measure == measure && x.LaneIndex == laneIndex && x.Width == width);
                        if (noteIndex >= 0)
                            holdIDlist[parsed.NoteIdentifier] = dr2Notes[noteIndex].ID;
                        else
                            holdIDlist.Remove(parsed.NoteIdentifier);
                    }

                    if (parsed.Notes[i].Item1 == 2) // End point references the ID we found earlier
                    {
                        if (holdIDlist.ContainsKey(parsed.NoteIdentifier))
                            dr2Notes.Add(new Dr2Note()
                            {
                                ID = NoteIncrementer.GetNextID(),
                                NoteType = SusToDr2Note(parsed.NoteClass, parsed.Notes[i].Item1),
                                Measure = (parsed.Measure + i * noteSub),
                                LaneIndex = parsed.LaneIndex,
                                Width = parsed.Notes[i].Item2,
                                ParentID = holdIDlist[parsed.NoteIdentifier]
                            });
                        else
                            failedNotes.Add(parsed);
                    }
                }
            }

            // Re-sort notes by measure
            dr2Notes.Sort((x, y) => x.Measure.CompareTo(y.Measure));

            // Reorder ID by measure
            var dictID = new Dictionary<int, int>();
            for (int i = 0; i < dr2Notes.Count; i++)
            {
                dictID.Add(dr2Notes[i].ID, i);
            }
            for (int i = 0; i < dr2Notes.Count; i++)
            {
                dr2Notes[i].ID = dictID[dr2Notes[i].ID];
                if(dr2Notes[i].ParentID != 0)
                    dr2Notes[i].ParentID = dictID[dr2Notes[i].ParentID];
            }

            // Add BPM changes into metadata
            foreach (var item in bpmChanges)
            {
                metadata.BpmChanges.Add(item.Value);
            }

            if (Globals.BezierNotesPresent)
                Console.WriteLine("WARNING: Bezier notes were found in this chart. This is not supported by *.dr2 currently and will appear as straight slides.");
            if (Globals.ComplexSpeedChangesPresent)
                Console.WriteLine("ERROR: Complex speed changes were found in this chart and this cannot be parsed at this time. It is not recommended to play this conversion.");
            if (Globals.MultipleBPMLinesPresent)
                Console.WriteLine("WARNING: Multiple BPM commands with the same keys were found. The first one was used and the rest were ignored.");
            if(Globals.AttributesTagPresent)
                Console.WriteLine("ERROR: Attribute tags were found in this chart and this cannot be parsed at this time. It is not recommended to play this conversion.");
            if (Globals.ErrorParsingTicksPerBeat)
                Console.WriteLine("ERROR: Could not parse ticks_per_beat, which makes speed changes incorrect. It is not recommended to play this conversion.");
            if (Globals.ErrorParsingSpeedChange)
                Console.WriteLine("ERROR: Speed change could not be parsed, unexpected number of arguments. Check original file");

            return dr2Notes;
        }

        static private NoteParse ParseLine(string line, bool forceSingleNum)
        {
            NoteParse parse;
            parse.Notes = new List<Tuple<int, int>>();
            parse.Values = new List<double>();

            string[] split = line.Split(':');
            string meta = split[0];
            string notes = split[1].Replace(" ", "");

            parse.Measure = Convert.ToDouble(meta.Substring(1, 3));
            parse.NoteClass = Convert.ToInt32(meta.Substring(4, 1));
            parse.LaneIndex = Convert.ToInt32(meta.Substring(5, 1), 16);
            if (meta.Length == 7)
                parse.NoteIdentifier = ParseAlphanumeric(line.Substring(6, 1));
            else
                parse.NoteIdentifier = -1;

            if (notes.Length == 1 || forceSingleNum)
            {
                parse.Values.Add(Convert.ToDouble(notes));
            }
            else
            {
                for (int i = 0; i < notes.Length; i += 2)
                {
                    parse.Notes.Add(new Tuple<int, int>(Convert.ToInt32(notes.Substring(i, 1)), ParseAlphanumeric(notes.Substring(i + 1, 1))));
                }
            }

            return parse;
        }

        static private NoteParse ParseLine(string line)
        {
            return ParseLine(line, false);
        }

        struct NoteParse
        {
            public double Measure;
            public int NoteClass;
            public int LaneIndex;
            public int NoteIdentifier;
            public List<Tuple<int, int>> Notes;
            public List<double> Values;
        }

        static private int ParseAlphanumeric(string s)
        {
            if (Regex.IsMatch(s, "[0-9]"))
                return Convert.ToInt32(s);
            else
                return Convert.ToInt32(s.ToLower()[0]) - 'a' + 10;
        }

        class Dr2Note
        {
            public int ID;  // starts at 0
            public int NoteType;
            public double Measure;
            public int LaneIndex;
            public int Width;
            public int AirDirection;
            public int ParentID;

        }

        static class NoteIncrementer
        {
            static private int _noteID = 0;
            static public int GetNextID() { _noteID++; return _noteID - 1; }
            static public void Reset() { _noteID = 0; }
        }

        static int SusToDr2Note(int noteClass, int noteType)
        {
            if(noteClass == 1)  // Single Note
            {
                switch (noteType)
                {
                    case 1: // Tap
                        return 1;
                    case 2: // ExTap
                        return 2;
                    case 3: // Flick
                        return 9;
                    case 4: // Damage
                        return 10;
                    default:
                        break;
                }
            }
            else if (noteClass == 5) // Air Motions
            {
                switch (noteType)
                {
                    case 1: // Up Air
                        return 1;
                    case 2: // Down Air
                        return 4;
                    case 3: // Upper Left Air
                        return 2;
                    case 4: // Upper Right Air
                        return 3;
                    case 5: // Down Left Air
                        return 5;
                    case 6: // Down Right Air
                        return 6;
                    default:
                        break;
                }
            }
            else if (noteClass == 2)    // Hold Notes
            {
                switch (noteType)
                {
                    case 1: // Start
                        return 3;
                    case 2: // End
                        return 4;
                    default:
                        break;
                }
            }
            else if (noteClass == 3)    // Slide Notes
            {
                switch (noteType)
                {
                    case 1: // Start
                        return 5;
                    case 2: // End
                    case 3: // Bend Point (Step)
                        return 7;
                    case 4: // Inflection point (nominally curved)
                        Globals.BezierNotesPresent = true;
                        return 6;
                    case 5: // Bend Point (invisible)
                        return 6;
                    default:
                        break;
                }
            }
            else if (noteClass == 4)    // Air-Action Lane
            {
                switch (noteType)
                {
                    case 1: // Start
                        return 5;
                    case 2: // End
                        return 8;
                    case 3: // Bend Point (or is this 4?)
                        return 6;
                    default:
                        break;
                }
            }
            return -1;
        }

        static void ExportDr2(string filename, List<Dr2Note> notes, Dr2Metadata metadata)
        {
            if (metadata.Difficulty == -1)
            {
                Console.WriteLine("No difficulty found. The file will end in \".X.dr2\" so you can search for it.");
                filename = filename.Replace(".dr2", ".X.dr2");
            }
            else
                filename = filename.Replace(".dr2", "." + metadata.Difficulty + ".dr2");

            using (StreamWriter sw = new StreamWriter(new FileStream(filename, FileMode.Create)))
            {
                sw.WriteLine("#NDNAME='{0}';", metadata.Designer);
                sw.WriteLine("#OFFSET={0};", metadata.Offset);
                sw.WriteLine("#BPM_NUMBER={0};", metadata.BpmChanges.Count);
                for (int i = 0; i < metadata.BpmChanges.Count; i++)
                {
                    sw.WriteLine("#BPM [{0}]={1}", i, metadata.BpmChanges[i].Bpm);
                    sw.WriteLine("#BPMS[{0}]={1}", i, metadata.BpmChanges[i].BpmStart);
                }
                if(metadata.SpeedChanges.Count == 0 || metadata.SpeedChanges.FirstOrDefault(x=> x.SpeedChangeIndex == 0) == null)
                    metadata.SpeedChanges.Insert(0, new Dr2SpeedChange() { SpeedChange = 1, SpeedChangeIndex = 0 });
                sw.WriteLine("#SCN={0}", metadata.SpeedChanges.Count);
                for (int i = 0; i < metadata.SpeedChanges.Count; i++)
                {
                    sw.WriteLine("#SC [{0}]={1}", i, metadata.SpeedChanges[i].SpeedChange);
                    sw.WriteLine("#SCI[{0}]={1}", i, metadata.SpeedChanges[i].SpeedChangeIndex);
                }
                if (metadata.Beat == -1)
                    metadata.Beat = 1;
                sw.WriteLine("#BEAT={0};", metadata.Beat);
                foreach (var note in notes)
                {
                    sw.WriteLine("<{0}><{1}><{2}><{3}><{4}><{5}><{6}>",
                        note.ID,
                        note.NoteType,
                        note.Measure.ToString("F5"),
                        note.LaneIndex,
                        note.Width,
                        note.AirDirection,
                        note.ParentID);
                }
            }
        }

        class SlideCollection
        {
            public List<Tuple<NoteParse, int>> Notes = new List<Tuple<NoteParse, int>>();
            public bool containsStart { get { return Notes.FirstOrDefault(x => x.Item1.NoteClass == 1) == null ? false : true; } }
            public bool containsEnd { get { return Notes.FirstOrDefault(x => x.Item1.NoteClass == 2) == null ? false : true; } }
        }

        class Dr2Metadata
        {
            public string Designer;
            public double Offset = 0;
            public double Beat = -1;
            public List<Dr2BpmChange> BpmChanges = new List<Dr2BpmChange>();
            public List<Dr2SpeedChange> SpeedChanges = new List<Dr2SpeedChange>();
            public int Difficulty = -1;
        }

        class Dr2BpmChange
        {
            public double Bpm;
            public double BpmStart; // Looks like it's just the measure? Unsure what BPMS stands for
        }

        class Dr2SpeedChange
        {
            public double SpeedChange;
            public double SpeedChangeIndex; // Looks like it's just the measure? Unsure what SCI stands for
        }

        static void SearchForFile(ref List<string> filenames, string startFolder, string fileFormat)
        {
            var folders = Directory.GetDirectories(startFolder);
            foreach (var folder in folders)
            {
                var files = Directory.GetFiles(folder);
                foreach (var file in files)
                {
                    if (file.EndsWith(fileFormat))
                        filenames.Add(file);
                }
                SearchForFile(ref filenames, folder, fileFormat);
            }
        }
    }

    
    public static class Globals
    {
        public static bool MultipleBPMLinesPresent = false;
        public static bool BezierNotesPresent = false;
        public static bool ComplexSpeedChangesPresent = false;
        public static bool AttributesTagPresent = false;
        public static bool ErrorParsingSpeedChange = false;
        public static bool ErrorParsingTicksPerBeat = false;

        public static void ResetFlags()
        {
            MultipleBPMLinesPresent = false;
            BezierNotesPresent = false;
            ComplexSpeedChangesPresent = false;
            AttributesTagPresent = false;
            ErrorParsingSpeedChange = false;
            ErrorParsingTicksPerBeat = false;
        }
    }
}
