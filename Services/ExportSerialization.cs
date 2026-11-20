using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using ObjectDetectTrackApp.Models;

namespace ObjectDetectTrackApp.Services
{
    public static class ExportSerialization
    {
        public static string SourceTime(double seconds)
        {
            if(!double.IsFinite(seconds) || seconds<0) return "";
            var t=TimeSpan.FromSeconds(seconds);
            return $"{(long)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
        }
        private static string Cell(string value) => value.IndexOfAny(new[]{';','"','\r','\n'})>=0
            ? "\""+value.Replace("\"","\"\"")+"\"" : value;
        public static string Csv(IEnumerable<Detection> rows)
        {
            var b=new StringBuilder("frame;time;class;confidence;x;y;width;height;track_id;selection_number;source_seconds;observed_at;analysis_mode\n");
            foreach(var d in rows) b.AppendLine(string.Join(";",new[]{
                d.FrameIndex.ToString(CultureInfo.InvariantCulture),SourceTime(d.SourceSeconds),Cell(d.ClassName),
                d.Confidence.ToString("F3",CultureInfo.InvariantCulture),d.X.ToString(CultureInfo.InvariantCulture),
                d.Y.ToString(CultureInfo.InvariantCulture),d.Width.ToString(CultureInfo.InvariantCulture),d.Height.ToString(CultureInfo.InvariantCulture),
                d.TrackId?.ToString(CultureInfo.InvariantCulture) ?? "",d.SelectionNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
                double.IsFinite(d.SourceSeconds) ? d.SourceSeconds.ToString("F6",CultureInfo.InvariantCulture) : "",
                d.Timestamp.ToString("O",CultureInfo.InvariantCulture),"view_observations"}));
            return b.ToString();
        }
        public static string Json(IEnumerable<Detection> rows) => JsonSerializer.Serialize(rows.Select(d=>new {
            frame=d.FrameIndex,time=SourceTime(d.SourceSeconds),cls=d.ClassName,conf=(float)Math.Round(d.Confidence,3),
            box=new {x=d.X,y=d.Y,w=d.Width,h=d.Height},selection_number=d.SelectionNumber,track=d.TrackId,
            source_seconds=double.IsFinite(d.SourceSeconds) ? (double?)d.SourceSeconds : null,
            observed_at=d.Timestamp.ToString("O",CultureInfo.InvariantCulture),analysis_mode="view_observations"
        }),new JsonSerializerOptions {WriteIndented=true});
    }
}
