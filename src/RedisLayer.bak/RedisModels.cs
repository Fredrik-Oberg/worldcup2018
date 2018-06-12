using System;
using System.Collections.Generic;

namespace RedisLayer
{
    public class Participant
    {
        public Participant()
        {
            
        }

        public Participant(string name)
        {
            Name = name;
            Matches = new List<Match>();
        }
        public string Name { get; set; }
        public string LastUpdated { get; set; }
        public List<Match> Matches { get; set; }
        public string Points { get; set; }
    }
    public class Match
    {
        public string HomeTeam { get; set; }
        public string AwayTeam { get; set; }
        public string Result { get; set; }
        public DateTime MatchStart { get; set; }
    }
}