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
            if (args.Length == 0 || args[0].Contains("--?") || args[0].Contains("/?") || args[0].Contains("--help"))
            {
                Console.WriteLine("Welcome to the SUS <-> DR2 Converter!");
                Console.WriteLine("Help will go here, eventually.");
                Console.WriteLine("Press Enter to close.");
                Console.ReadLine();
            }
            else
            {
                Console.WriteLine("Parsing {0}...", args[0]);
            }

            // Check which direction we're going
            if(args[0].EndsWith(".sus"))
            {
                Dr2Metadata meta;
                var notes = ParseSus(args[0], out meta);
                string exportName = args[0].Replace(".sus", ".dr2");
                ExportDr2(exportName, notes, meta);
            }
            else if (args[0].EndsWith(".dr2"))
            {

            }
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

            Dictionary<int, int> slideIDlist = new Dictionary<int, int>();
            List<List<Tuple<NoteParse, int>>> slideNotes = new List<List<Tuple<NoteParse, int>>>();

            List<NoteParse> failedNotes = new List<NoteParse>();

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
                        bpmChanges.Add(Convert.ToInt32(split[0]), new Dr2BpmChange() { Bpm = Convert.ToDouble(split[1]), BpmStart = -1 });
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
                                        // Definitely create a new list
                                        slideNotes.Add(new List<Tuple<NoteParse, int>>());
                                        slideIDlist[parsed.NoteIdentifier] = slideNotes.Count - 1;
                                        slideNotes[slideIDlist[parsed.NoteIdentifier]].Add(new Tuple<NoteParse, int>(parsed, i));
                                        break;
                                    case 2: // End Note, can't guarantee this is the true end
                                    case 3: // Bend Note Step
                                    case 5: // Bend Note Invisible
                                        slideNotes[slideIDlist[parsed.NoteIdentifier]].Add(new Tuple<NoteParse, int>(parsed, i));
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
                        var parsed = ParseLine(line);

                        if (metadata.Beat == -1)
                            metadata.Beat = parsed.Notes[0].Item1 / 4;
                    }
                }
            }

            // Generate slide notes
            foreach (var list in slideNotes)
            {
                list.Sort((x, y) => (x.Item1.Measure + x.Item2 * (1.0 / x.Item1.Notes.Count)).CompareTo(y.Item1.Measure + y.Item2 * (1.0 / y.Item1.Notes.Count)));
                foreach (var tuple in list)
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

            // Add BPM changes into metadata
            foreach (var item in bpmChanges)
            {
                metadata.BpmChanges.Add(item.Value);
            }

            return dr2Notes;
        }

        static private NoteParse ParseLine(string line)
        {
            NoteParse parse;
            parse.Notes = new List<Tuple<int, int>>();

            string[] split = line.Split(':');
            string meta = split[0];
            string notes = split[1].Replace(" ", "");

            parse.Measure = Convert.ToDouble(meta.Substring(1, 3));
            parse.NoteClass = Convert.ToInt32(meta.Substring(4, 1));
            parse.LaneIndex = Convert.ToInt32(meta.Substring(5, 1), 16);
            if (meta.Length == 7)
                parse.NoteIdentifier = ParseIdentifier(line.Substring(6, 1));
            else
                parse.NoteIdentifier = -1;

            if (notes.Length == 1)
            {
                parse.Notes.Add(new Tuple<int, int>(Convert.ToInt32(notes), -1));
            }
            else
            {
                for (int i = 0; i < notes.Length; i += 2)
                {
                    parse.Notes.Add(new Tuple<int, int>(Convert.ToInt32(notes.Substring(i, 1)), ParseNoteWidth(notes.Substring(i + 1, 1))));
                }
            }

            return parse;
        }

        struct NoteParse
        {
            public double Measure;
            public int NoteClass;
            public int LaneIndex;
            public int NoteIdentifier;
            public List<Tuple<int, int>> Notes;
        }

        static private int ParseNoteWidth(string s)
        {
            if (Regex.IsMatch(s, "[0-9]"))
                return Convert.ToInt32(s);
            else
                return Convert.ToInt32(s.ToLower()[0]) - 'a' + 10;
        }

        static private int ParseIdentifier(string s)
        {
            if (Regex.IsMatch(s, "[0-9]"))
                return Convert.ToInt32(s);
            else
                return Convert.ToInt32(s.ToUpper()[0]) - 'A' + 10;
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
            using (StreamWriter sw = new StreamWriter(new FileStream(filename, FileMode.CreateNew)))
            {
                sw.WriteLine("#NDNAME='{0}';", metadata.Designer);
                sw.WriteLine("#OFFSET={0};", metadata.Offset);
                sw.WriteLine("#BPM_NUMBER={0};", metadata.BpmChanges.Count);
                for (int i = 0; i < metadata.BpmChanges.Count; i++)
                {
                    sw.WriteLine("#BPM [{0}]={1}", i, metadata.BpmChanges[i].Bpm);
                    sw.WriteLine("#BPMS[{0}]={1}", i, metadata.BpmChanges[i].BpmStart);
                }
                if(metadata.SpeedChanges.Count == 0)
                    metadata.SpeedChanges.Add(new Dr2SpeedChange() { SpeedChange = 1, SpeedChangeIndex = 0 });
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

        public class Dr2Metadata
        {
            public string Designer;
            public double Offset = 0;
            public double Beat = -1;
            public List<Dr2BpmChange> BpmChanges = new List<Dr2BpmChange>();
            public List<Dr2SpeedChange> SpeedChanges = new List<Dr2SpeedChange>();
        }

        public class Dr2BpmChange
        {
            public double Bpm;
            public double BpmStart; // Looks like it's just the measure? Unsure what BPMS stands for
        }

        public class Dr2SpeedChange
        {
            public double SpeedChange;
            public double SpeedChangeIndex; // Looks like it's just the measure? Unsure what SCI stands for
        }
    }
}
