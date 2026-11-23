using System.Collections.Generic;
using System.Text;

namespace HandFleet
{
    // Local top-ten table, stored as one "score|date;score|date" string so it survives in a single preference.
    public sealed class ArcadeLeaderboard
    {
        public const int Size=10;
        public readonly List<(int Score,string Date)> Entries=new();
        public static ArcadeLeaderboard Parse(string stored)
        {
            var board=new ArcadeLeaderboard();
            if(string.IsNullOrEmpty(stored)) return board;
            foreach(var item in stored.Split(';'))
            {
                var parts=item.Split('|');
                if(parts.Length>0&&int.TryParse(parts[0],out int score)&&score>0) board.Entries.Add((score,parts.Length>1?parts[1]:""));
            }
            board.Entries.Sort((a,b)=>b.Score.CompareTo(a.Score));
            if(board.Entries.Count>Size) board.Entries.RemoveRange(Size,board.Entries.Count-Size);
            return board;
        }
        // Returns the 1-based rank of the new score, or 0 when it did not make the table.
        public int Insert(int score,string date)
        {
            if(score<=0) return 0;
            int rank=0; while(rank<Entries.Count&&Entries[rank].Score>=score) rank++;
            if(rank>=Size) return 0;
            Entries.Insert(rank,(score,date??""));
            if(Entries.Count>Size) Entries.RemoveAt(Size);
            return rank+1;
        }
        public int Best=>Entries.Count>0?Entries[0].Score:0;
        public string Serialize()
        {
            var text=new StringBuilder();
            for(int i=0;i<Entries.Count;i++) { if(i>0) text.Append(';'); text.Append(Entries[i].Score).Append('|').Append(Entries[i].Date); }
            return text.ToString();
        }
        public string Display(int highlight)
        {
            var text=new StringBuilder("HIGH SCORES\n");
            if(Entries.Count==0) return text.Append("\nNO SCORES YET").ToString();
            for(int i=0;i<Entries.Count;i++)
                text.Append(i+1==highlight?"> ":"  ").Append((i+1).ToString().PadLeft(2)).Append("   ").Append(Entries[i].Score.ToString("000000")).Append("   ").Append(Entries[i].Date).Append(i+1==highlight?" <":"").Append('\n');
            return text.ToString();
        }
    }
}
