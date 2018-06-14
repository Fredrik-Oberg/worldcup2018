using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using RedisLayer;

namespace web.Controllers
{
    [Route("api/[controller]")]
    public class ResultsController : Controller
    {
        [HttpGet("[action]")]
        public IEnumerable<Participant> All()
        {
            var redis = new Redis(10);
            var keys = redis.GetAllKeys();
            var participants = keys.Where(x => x.Contains("participant", StringComparison.OrdinalIgnoreCase))
                .Select(x => redis.GetRedisValue<Participant>(x)).ToList();
            var orderedParticipants = new List<Participant>();
            foreach (var participant in participants)
            {
                var orderedMatches = new List<Match>(participant.Matches.OrderByDescending(x => x.MatchStart));
                participant.Matches = orderedMatches;
                orderedParticipants.Add(participant);
            }

            return orderedParticipants.OrderByDescending(x => x.Points);
        }

        [HttpGet("[action]")]
        public IEnumerable<ExpandoObject> Latest()
        {
            var all = All().ToList();
            var first = all.First();
            var matchStart = first?.Matches?.OrderByDescending(u => u.MatchStart).FirstOrDefault()?.MatchStart;

            //var parsedGameTime = DateTime.ParseExact("2018-06-25 18:00", "yyyy-MM-dd HH:mm",
            //    CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

            //var matchStart = DateTime.SpecifyKind(parsedGameTime, DateTimeKind.Utc);
            
            
            //using (StreamReader r = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "/assets/schedule.json"))
            //{
            //    string json = r.ReadToEnd();
            //    var schedules = JsonConvert.DeserializeObject<IList<Schedule>>(json);
            //    foreach (var schedule in schedules)
            //    {
            //        var parsedGameTime = DateTime.ParseExact(schedule.Start, "yyyy-MM-dd HH:mm",
            //            CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

            //        var scheduledMatchUtc = DateTime.SpecifyKind(parsedGameTime, DateTimeKind.Utc);
            //        if (scheduledMatchUtc == matchStart)
            //        {
            //            concurrentGames = schedule.ConcurrentGames;
            //            break;
            //        }
            //    }


            var result = new List<ExpandoObject>();
            foreach (var match in first.Matches.Where(x => x.MatchStart == matchStart))
            {
                var parent = new ExpandoObject();
                parent.TryAdd("matchStart", match.MatchStart);
                parent.TryAdd("homeTeam", match.HomeTeam);
                parent.TryAdd("awayTeam", match.AwayTeam);

                var participants = all.Select(p => new
                {
                    LastUpdated = p.LastUpdated,
                    Name = p.Name,
                    Points = p.Points,
                    Result = p.Matches.FirstOrDefault(x => x.MatchStart == match.MatchStart && x.HomeTeam == match.HomeTeam)?.Result
                }).ToList();

                parent.TryAdd("participants", participants);

                result.Add(parent);
            }

            return result;
        }
    }
}
