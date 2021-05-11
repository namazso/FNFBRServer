using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
#pragma warning disable 8632

namespace FNFBRServer
{
    class Section
    {
        public object[][] sectionNotes { get; set; }
        public int bpm { get; set; }
        public bool changeBPM { get; set; }
        public bool altAnim { get; set; }

        public int? lengthInSteps { get; set; }
        public int? typeOfSection { get; set; }
        public bool? mustHitSection { get; set; }
    }

    class Song
    {
        public string song { get; set; }
        public int bpm { get; set; }
        public Section[]? notes { get; set; }


        public bool? needsVoices { get; set; }
        public float? speed { get; set; }

        public string? gfVersion { get; set; }
        public string? noteStyle { get; set; }
        public string? stage { get; set; }

        public void RemoveDefaults()
        {
            if (speed is 1.0f)
                speed = null;
            if (gfVersion is "gf")
                gfVersion = null;
            if (noteStyle is "normal")
                noteStyle = null;
            if (stage is "stage")
                stage = null;
        }
    }

    class Chart
    {
        public Song song { get; set; }
        public Section[]? notes { get; set; }

        public void RemoveDefaults()
        {
            song.RemoveDefaults();
            var innerCount = song.notes?.Length ?? 0;
            if (innerCount == 0)
                song.notes = notes;
            notes = null;
        }

        public static byte[] FixChart(byte[] inFile, string folder)
        {
            var inString = System.Text.Encoding.UTF8.GetString(inFile);
            var chart = JsonSerializer.Deserialize<Chart>(inString);
            chart.song.song = folder;
            chart.RemoveDefaults();
            var options = new JsonSerializerOptions
            {
                IgnoreNullValues = true,
                WriteIndented = false
            };
            var outString = JsonSerializer.Serialize(chart, options);
            return System.Text.Encoding.UTF8.GetBytes(outString);
        }
    }
}
